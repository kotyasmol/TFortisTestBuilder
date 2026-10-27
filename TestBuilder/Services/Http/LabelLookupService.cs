using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services.Http;

public sealed record LabelData(int Kind, string Serial, string Mac);

public sealed class LabelLookupService(IHttpRequestService http)
{
    public static string NormalizeMac(string value)
    {
        var hex = value.Trim().Replace(":", "").Replace("-", "");
        if (hex.Length != 12 || !hex.All(Uri.IsHexDigit)) throw new InvalidOperationException("Введите MAC из 12 шестнадцатеричных цифр.");
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2).ToUpperInvariant()));
    }

    public async Task<LabelData> FindAsync(string server, string query, bool byMac, CancellationToken token)
    {
        query = byMac ? NormalizeMac(query) : query.Trim();
        if (!byMac && (!long.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var serial) || serial <= 0))
            throw new InvalidOperationException("Введите полный серийный номер изделия.");
        var root = ServerBaseUrlResolver.NormalizeForHttp(server).TrimEnd('/');
        if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Укажите адрес сервера в настройках.");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/api.svc", StringComparison.OrdinalIgnoreCase)) { }
        else if (path.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) root += "/api.svc";
        else root += "/api/api.svc";
        var result = await http.GetAsync(root + "/getdevicehistorylist?findParam=" + (byMac ? "mac" : "serial") +
            "&findVal=" + Uri.EscapeDataString(query), TimeSpan.FromSeconds(20), token);
        if (!result.IsSuccessStatusCode) throw new InvalidOperationException($"Ошибка сервера: HTTP {result.StatusCode}. {result.ErrorMessage}");
        return Parse(result.Body, query, byMac);
    }

    internal static LabelData Parse(string body, string query, bool byMac)
    {
        if (body.Trim().Trim('"').Equals("NoAuth", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Сервер требует авторизацию. Запустите программу через лаунчер.");
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Сервер вернул неизвестный формат истории изделия.");
        var matches = new List<LabelData>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("device", out var device) ||
                device.ValueKind != JsonValueKind.Object) continue;
            var serial = Field(device, "serial", "serial_num", "serialNumber", "serial_number");
            var mac = Field(device, "mac", "mac_address", "default_mac", "macAddress");
            if (!int.TryParse(serial, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) continue;
            var kind = number is >= 3200000 and <= 3265535 ? 0 : number is >= 600000 and <= 665535 ? 1 : -1;
            if (kind < 0 || string.IsNullOrWhiteSpace(mac)) continue;
            mac = NormalizeMac(mac);
            if (byMac ? mac != NormalizeMac(query) : number.ToString(CultureInfo.InvariantCulture) != query.TrimStart('0')) continue;
            matches.Add(new(kind, number.ToString(CultureInfo.InvariantCulture), mac));
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
