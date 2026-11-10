using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services.Http;

public sealed record LabelData(int Kind, string Serial, string Mac);

public sealed class LabelLookupService(IHttpRequestService http, List<LabelTemplateDefinition>? workstationTemplates = null)
{
    public static string NormalizeMac(string value)
    {
        if (!LabelPrintContent.TryNormalizeMac(value, out var mac))
            throw new InvalidOperationException("Введите MAC из 12 шестнадцатеричных цифр.");
        return mac;
    }

    public async Task<LabelData> FindAsync(string server, string query, bool byMac, CancellationToken token)
    {
        query = byMac ? NormalizeMac(query) : query.Trim();
        if (!byMac && (!int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var serial) || serial <= 0))
            throw new InvalidOperationException("Введите серийный номер изделия.");
        var root = ServerBaseUrlResolver.NormalizeForHttp(server);
        if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Укажите адрес сервера в настройках.");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/getdevicehistorylist", StringComparison.OrdinalIgnoreCase))
        {
            if (path.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) path += "/api.svc";
            else if (!path.EndsWith("/api.svc", StringComparison.OrdinalIgnoreCase)) path += "/api/api.svc";
            path += "/getdevicehistorylist";
        }
        var endpoint = new UriBuilder(uri) {
            Path = path, Query = $"findParam={(byMac ? "mac" : "serial")}&findVal={Uri.EscapeDataString(query)}", Fragment = ""
        };
        var result = await http.GetAsync(endpoint.Uri.AbsoluteUri, TimeSpan.FromSeconds(20), token);
        if (result.StatusCode is 401 or 403)
            throw new InvalidOperationException("Сервер отклонил авторизацию. Запустите программу через лаунчер.");
        if (!result.IsSuccessStatusCode)
            throw new InvalidOperationException(result.StatusCode is { } status ? $"Ошибка сервера этикеток (HTTP {status})." : result.ErrorMessage);
        return Parse(result.Body, query, byMac, workstationTemplates);
    }

    internal static LabelData Parse(string body, string query, bool byMac, List<LabelTemplateDefinition>? workstationTemplates = null)
    {
        if (body.Trim().Trim('"').Equals("NoAuth", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Сервер требует авторизацию. Запустите программу через лаунчер.");
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Сервер вернул неизвестный формат истории изделия.");
        if (document.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException("Сервер не нашёл изделие. Проверьте серийный номер или MAC.");
        var templates = workstationTemplates ?? AppSettings.Instance.LabelTemplates;
        LabelTemplateCatalog.Validate(templates);
        var match = byMac ? NormalizeMac(query) : query.TrimStart('0');
        var matches = new List<LabelData>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("device", out var device) ||
                device.ValueKind != JsonValueKind.Object) continue;
            var serial = Field(device, "serial", "serial_num", "serialNumber", "serial_number");
            if (!int.TryParse(serial, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) continue;
            var mac = Field(device, "mac", "mac_address", "default_mac", "macAddress");
            var hasMac = LabelPrintContent.TryNormalizeMac(mac, out var normalizedMac);
            var fullSerial = number.ToString(CultureInfo.InvariantCulture);
            if (byMac ? !hasMac || normalizedMac != match : fullSerial != match) continue;
            var kind = templates.FindIndex(template => template.MatchesServerSerial(number));
            if (kind < 0 || templates[kind].RequiresMac && !hasMac) continue;
            matches.Add(new(kind, fullSerial, normalizedMac));
        }
        var distinct = matches.Distinct().ToArray();
        if (distinct.Length != 1)
            throw new InvalidOperationException(distinct.Length == 0
                ? "Не найдена полная этикетка: в ответе нужны совпадающий серийник/MAC и поддерживаемая модель."
                : "Сервер вернул несколько разных этикеток. Уточните запрос.");
        return distinct[0];
    }

    private static string Field(JsonElement element, params string[] names)
    {
        foreach (var field in element.EnumerateObject())
            if (names.Contains(field.Name, StringComparer.OrdinalIgnoreCase) && field.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                return field.Value.ToString().Trim();
        return string.Empty;
    }
}
