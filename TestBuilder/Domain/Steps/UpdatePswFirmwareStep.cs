using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Http;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps;

/// <summary>Web firmware update for legacy PSW devices; Pro devices use another protocol.</summary>
public sealed class UpdatePswFirmwareStep : ITestStep
{
    private readonly IHttpRequestService _http;
    private readonly ILogger _logger;
    private readonly string _baseUrl;
    private readonly string _firmwarePath;
    private readonly string _targetVersion;
    private readonly bool _forceUpdate;
    private readonly string _versionVariable;
    private readonly Func<HttpClient> _createClient;
    private readonly Func<TestContext, CancellationToken, Task<StepResult>> _refreshSelfTest;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public UpdatePswFirmwareStep(IHttpRequestService http, ILogger logger, string baseUrl,
        string firmwarePath, string targetVersion, string versionVariable, bool forceUpdate)
        : this(http, logger, baseUrl, firmwarePath, targetVersion, versionVariable, forceUpdate,
            () => new HttpClient(new HttpClientHandler { UseProxy = false })
            { Timeout = TimeSpan.FromSeconds(120) }, null, null)
    { }

    internal UpdatePswFirmwareStep(IHttpRequestService http, ILogger logger, string baseUrl,
        string firmwarePath, string targetVersion, string versionVariable, bool forceUpdate,
        Func<HttpClient> createClient,
        Func<TestContext, CancellationToken, Task<StepResult>>? refreshSelfTest,
        Func<TimeSpan, CancellationToken, Task>? delay)
    {
        _http = http;
        _logger = logger;
        _baseUrl = baseUrl?.TrimEnd('/') ?? string.Empty;
        _firmwarePath = firmwarePath ?? string.Empty;
        _targetVersion = targetVersion ?? string.Empty;
        _forceUpdate = forceUpdate;
        _versionVariable = versionVariable ?? string.Empty;
        _createClient = createClient;
        _refreshSelfTest = refreshSelfTest ?? RefreshSelfTestAsync;
        _delay = delay ?? Task.Delay;
    }

    public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        var stage = "проверка параметров";
        context.SetVariable("Firmware.Updated", false);
        context.SetVariable("Firmware.Error", string.Empty);
        context.SetVariable("Firmware.Stage", stage);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttp || baseUri.AbsolutePath != "/" ||
            !TryParseVersion(_targetVersion, out var target))
            return Fail(context, "Некорректный адрес коммутатора или версия прошивки.");

        if (!context.Variables.TryGetValue(_versionVariable, out var rawVersion) ||
            !TryParseVersion(rawVersion?.ToString(), out var current, deviceValue: true))
            return Fail(context, $"Нет корректной версии в переменной {_versionVariable}.");

        context.SetVariable("Firmware.Before", rawVersion?.ToString() ?? string.Empty);
        context.SetVariable("Firmware.Target", _targetVersion);
        _logger.Info($"[FW] Текущая версия '{rawVersion}' (код {current}), цель '{_targetVersion}' (код {target}), принудительное обновление: {_forceUpdate}.");
        if (current == target || (!_forceUpdate && current > target))
        {
            context.SetVariable("Firmware.Stage", "пропущено");
            _logger.Info($"[FW] Обновление пропущено: установленная версия уже подходит. HTTP-запросов прошивки не было.");
            return StepResult.True;
        }

        try
        {
            // Validate the local image before the first destructive HTTP request.
            var expectedName = $"sw407-{_targetVersion}-";
            if (!File.Exists(_firmwarePath) ||
                !Path.GetFileName(_firmwarePath).StartsWith(expectedName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(_firmwarePath), ".img", StringComparison.OrdinalIgnoreCase))
                return Fail(context, $"Не найден образ {expectedName}*.img: {_firmwarePath}");

            await using var image = new FileStream(_firmwarePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (image.Length == 0)
                return Fail(context, "Файл прошивки пуст.");
            context.SetVariable("Firmware.ImageSizeBytes", image.Length);
            _logger.Info($"[FW] Файл открыт: {Path.GetFileName(_firmwarePath)}, {image.Length} байт. SHA-256 не проверяется.");

            using var client = _createClient();
            client.DefaultRequestHeaders.ExpectContinue = false;
            stage = "очистка памяти";
            context.SetVariable("Firmware.Stage", stage);
            var requestTimer = Stopwatch.StartNew();
            _logger.Info($"[FW] GET {_baseUrl}/clear.shtml: начало очистки памяти.");
            using (var clear = await client.GetAsync(new Uri(baseUri, "/clear.shtml"), cancellationToken))
            {
                await LogResponseAsync("Очистка", clear, requestTimer.ElapsedMilliseconds, cancellationToken);
                clear.EnsureSuccessStatusCode();
            }
            _logger.Info("[FW] Пауза 10 с после очистки памяти.");
            await _delay(TimeSpan.FromSeconds(10), cancellationToken);

            stage = "загрузка образа";
            context.SetVariable("Firmware.Stage", stage);
            using var form = BuildLegacyMultipart(image, Path.GetFileName(_firmwarePath));
            requestTimer.Restart();
            _logger.Info($"[FW] POST {_baseUrl}/mngt/update.shtml: {form.Headers.ContentLength} байт multipart, образ {image.Length} байт.");
            using (var upload = await client.PostAsync(new Uri(baseUri, "/mngt/update.shtml"), form, cancellationToken))
            {
                await LogResponseAsync("Загрузка", upload, requestTimer.ElapsedMilliseconds, cancellationToken);
                upload.EnsureSuccessStatusCode();
            }
            _logger.Info("[FW] Пауза 10 с после передачи образа.");
            await _delay(TimeSpan.FromSeconds(10), cancellationToken);

            stage = "подтверждение обновления";
            context.SetVariable("Firmware.Stage", stage);
            requestTimer.Restart();
            _logger.Info($"[FW] GET {_baseUrl}/mngt/update.shtml?Update=Update: подтверждение прошивки.");
            using (var confirm = await client.GetAsync(new Uri(baseUri, "/mngt/update.shtml?Update=Update"), cancellationToken))
            {
                await LogResponseAsync("Подтверждение", confirm, requestTimer.ElapsedMilliseconds, cancellationToken);
                confirm.EnsureSuccessStatusCode();
            }
            _logger.Info("[FW] Пауза 40 с на перезапуск коммутатора.");
            await _delay(TimeSpan.FromSeconds(40), cancellationToken);

            stage = "повторный selftest";
            context.SetVariable("Firmware.Stage", stage);
            _logger.Info("[FW] Запрос новой /test.shtml и проверка версии после перезапуска.");
            // SelfTestCheck only writes fields present on the page. Remove the old version
            // so a missing field cannot be mistaken for a post-update reading.
            context.Variables.Remove(_versionVariable);
            if (await _refreshSelfTest(context, cancellationToken) != StepResult.True)
                return Fail(context, "После обновления не удалось прочитать новую тестовую страницу.");
            if (!context.Variables.TryGetValue(_versionVariable, out var updatedRaw) ||
                !TryParseVersion(updatedRaw?.ToString(), out var updated, deviceValue: true))
                return Fail(context, $"На новой тестовой странице нет корректной версии ПО ({_versionVariable}).");
            _logger.Info($"[ШАГ] Версия после обновления: '{updatedRaw}' (код {updated}); ожидалось {_targetVersion} (код {target}).");
            if (_forceUpdate ? updated != target : updated < target)
                return Fail(context, $"После обновления версия ПО '{updatedRaw}' не совпала с {_targetVersion}.");

            context.SetVariable("Firmware.Updated", true);
            context.SetVariable("Firmware.Stage", "завершено");
            context.SetVariable("Firmware.ElapsedMs", total.ElapsedMilliseconds);
            _logger.Info($"[OK] Прошивка обновлена до {updatedRaw}; всего {total.ElapsedMilliseconds} мс.");
            return StepResult.True;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { return Fail(context, $"Обновление прошивки прервано на этапе '{stage}' через {total.ElapsedMilliseconds} мс: {ex}"); }
    }

    private async Task LogResponseAsync(string stage, HttpResponseMessage response, long elapsedMs,
        CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "не указан";
        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "не указан";
        var details = $"[FW] {stage}: HTTP {(int)response.StatusCode}, {elapsedMs} мс, " +
            $"Content-Type={mediaType}, итоговый URL={finalUrl}.";
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var preview = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
            // The update page starts with JavaScript; a rejection or status is often
            // further down in the HTML. Keep both ends of a long response in the log.
            if (preview.Length > 4000)
                preview = preview[..2000] + $" … [пропущено {preview.Length - 4000} символов] … " + preview[^2000..];
            if (preview.Length > 0) details += $" Ответ: {preview}";
        }
        _logger.Info(details);
    }

    internal static HttpContent BuildLegacyMultipart(Stream image, string fileName)
    {
        // The old Qt uploader sends this exact multipart layout to the sw407 web server.
        const string boundary = "---------------------------723690991551375881941828858";
        var prefix = Encoding.UTF8.GetBytes(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"action\"\r\n\r\n\r\n" +
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"updatefile\"; filename=\"{fileName}\"\r\n" +
            "Content-Type: application/octet-stream;\r\n\r\n");
        var suffix = Encoding.UTF8.GetBytes(
            $"\r\n--{boundary}\r\n--{boundary}\r\n" +
            $"Content-Disposition: form-data; name=\"updatefile\"; filename=\"{fileName}\"\r\n\r\n");
        using var payload = new MemoryStream();
        payload.Write(prefix);
        image.CopyTo(payload);
        payload.Write(suffix);
        var content = new ByteArrayContent(payload.ToArray());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
        return content;
    }

    private async Task<StepResult> RefreshSelfTestAsync(TestContext context, CancellationToken cancellationToken)
    {
        var step = new SelfTestCheckStep(_http, _logger, _baseUrl + "/test.shtml", 180000,
            "Dut", "init_ok=1..1\ndev_type=6..6", true);
        return await step.ExecuteAsync(context, cancellationToken);
    }

    private StepResult Fail(TestContext context, string error)
    {
        context.SetVariable("Firmware.Error", error);
        context.HasCriticalError = true;
        _logger.Warning($"[ОШИБКА] {error}");
        return StepResult.False;
    }

    internal static bool TryParseVersion(string? value, out int version, bool deviceValue = false)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        var parts = text.Split('.');
        if (parts.Length == 3 && int.TryParse(parts[0], out var major) &&
            int.TryParse(parts[1], out var minor) && int.TryParse(parts[2], out var patch) &&
            major is >= 0 and <= 15 && minor is >= 0 and <= 15 && patch is >= 0 and <= 255)
        {
            version = (major << 12) | (minor << 8) | patch;
            return true;
        }
        var hasHexPrefix = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (hasHexPrefix) text = text[2..];
        // DUT selftest encodes 0.2.8 as 208 and 0.2.12 as 20c: both are hex.
        // Profile numbers such as 520 remain decimal unless explicitly prefixed.
        if (hasHexPrefix || text.IndexOfAny("abcdefABCDEF".ToCharArray()) >= 0 ||
            (deviceValue && text.Length == 3))
            return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out version);
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }
}
