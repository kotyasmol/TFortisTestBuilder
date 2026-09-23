using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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
    private readonly string _expectedSha256;
    private readonly string _versionVariable;
    private readonly Func<HttpClient> _createClient;
    private readonly Func<TestContext, CancellationToken, Task<StepResult>> _refreshSelfTest;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public UpdatePswFirmwareStep(IHttpRequestService http, ILogger logger, string baseUrl,
        string firmwarePath, string targetVersion, string versionVariable, string expectedSha256)
        : this(http, logger, baseUrl, firmwarePath, targetVersion, versionVariable, expectedSha256,
            () => new HttpClient { Timeout = TimeSpan.FromSeconds(120) }, null, null)
    { }

    internal UpdatePswFirmwareStep(IHttpRequestService http, ILogger logger, string baseUrl,
        string firmwarePath, string targetVersion, string versionVariable, string expectedSha256,
        Func<HttpClient> createClient,
        Func<TestContext, CancellationToken, Task<StepResult>>? refreshSelfTest,
        Func<TimeSpan, CancellationToken, Task>? delay)
    {
        _http = http;
        _logger = logger;
        _baseUrl = baseUrl?.TrimEnd('/') ?? string.Empty;
        _firmwarePath = firmwarePath ?? string.Empty;
        _targetVersion = targetVersion ?? string.Empty;
        _expectedSha256 = expectedSha256 ?? string.Empty;
        _versionVariable = versionVariable ?? string.Empty;
        _createClient = createClient;
        _refreshSelfTest = refreshSelfTest ?? RefreshSelfTestAsync;
        _delay = delay ?? Task.Delay;
    }

    public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
    {
        context.SetVariable("Firmware.Updated", false);
        context.SetVariable("Firmware.Error", string.Empty);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttp || baseUri.AbsolutePath != "/" ||
            !TryParseVersion(_targetVersion, out var target))
            return Fail(context, "Некорректный адрес коммутатора или версия прошивки.");

        if (!context.Variables.TryGetValue(_versionVariable, out var rawVersion) ||
            !TryParseVersion(rawVersion?.ToString(), out var current))
            return Fail(context, $"Нет корректной версии в переменной {_versionVariable}.");

        context.SetVariable("Firmware.Before", rawVersion?.ToString() ?? string.Empty);
        context.SetVariable("Firmware.Target", _targetVersion);
        if (current >= target)
        {
            _logger.Info($"[OK] Прошивка {rawVersion} уже не старее {_targetVersion}; обновление пропущено.");
            return StepResult.True;
        }

        try
        {
            // Validate everything available locally before the first destructive HTTP request.
            var expectedName = $"sw407-{_targetVersion}-";
            if (!File.Exists(_firmwarePath) ||
                !Path.GetFileName(_firmwarePath).StartsWith(expectedName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(_firmwarePath), ".img", StringComparison.OrdinalIgnoreCase))
                return Fail(context, $"Не найден образ {expectedName}*.img: {_firmwarePath}");

            await using var image = new FileStream(_firmwarePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (image.Length == 0 || _expectedSha256.Length != 64 ||
                !Convert.ToHexString(SHA256.HashData(image))
                    .Equals(_expectedSha256, StringComparison.OrdinalIgnoreCase))
                return Fail(context, "Контрольная сумма образа прошивки не совпала с профилем.");
            image.Position = 0;

            using var client = _createClient();
            _logger.Info($"[ШАГ] Обновление PSW {rawVersion} → {_targetVersion}: очистка памяти.");
            using (var clear = await client.GetAsync(new Uri(baseUri, "/clear.shtml"), cancellationToken))
                clear.EnsureSuccessStatusCode();
            await _delay(TimeSpan.FromSeconds(10), cancellationToken);

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(string.Empty), "action");
            using var imageContent = new StreamContent(image);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(imageContent, "updatefile", Path.GetFileName(_firmwarePath));
            _logger.Info($"[ШАГ] Загрузка образа {Path.GetFileName(_firmwarePath)}.");
            using (var upload = await client.PostAsync(new Uri(baseUri, "/mngt/update.shtml"), form, cancellationToken))
                upload.EnsureSuccessStatusCode();
            await _delay(TimeSpan.FromSeconds(10), cancellationToken);

            _logger.Info("[ШАГ] Подтверждение обновления ПО.");
            using (var confirm = await client.GetAsync(new Uri(baseUri, "/mngt/update.shtml?Update=Update"), cancellationToken))
                confirm.EnsureSuccessStatusCode();
            await _delay(TimeSpan.FromSeconds(40), cancellationToken);

            if (await _refreshSelfTest(context, cancellationToken) != StepResult.True ||
                !context.Variables.TryGetValue(_versionVariable, out var updatedRaw) ||
                !TryParseVersion(updatedRaw?.ToString(), out var updated) || updated < target)
                return Fail(context, $"После обновления версия ПО не достигла {_targetVersion}.");

            context.SetVariable("Firmware.Updated", true);
            _logger.Info($"[OK] Прошивка обновлена до {updatedRaw}.");
            return StepResult.True;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { return Fail(context, $"Обновление прошивки прервано: {ex.Message}"); }
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

    internal static bool TryParseVersion(string? value, out int version)
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
        if (hasHexPrefix || text.IndexOfAny("abcdefABCDEF".ToCharArray()) >= 0)
            return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out version);
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }
}
