using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Domain.Steps;
public static class ResponseParser
{
    public static string Expand(string text, TestContext context)
        => Regex.Replace(text ?? "", @"\$\{([^{}\r\n]+)\}", match =>
        {
            if (!context.Variables.TryGetValue(match.Groups[1].Value, out var value)) throw new InvalidOperationException("Не задана переменная: " + match.Groups[1].Value);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public static object Parse(string input, string mode, string selector, string valueType)
    {
        if (input.Length > 1_048_576 || selector.Length > 8192) throw new InvalidOperationException("Слишком большой ответ или шаблон.");
        string value;
        if (mode == "Regex")
        {
            var match = Regex.Match(input, selector, RegexOptions.CultureInvariant | RegexOptions.Multiline, TimeSpan.FromMilliseconds(200));
            if (!match.Success) throw new InvalidOperationException("Ответ не соответствует регулярному выражению.");
            value = match.Groups["value"].Success ? match.Groups["value"].Value : match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
        }
        else if (mode == "Json") value = ExtractJson(input, selector);
        else if (mode == "Text") value = input.Trim();
        else throw new InvalidOperationException("Формат разбора: Json, Regex или Text.");
        return valueType switch
        {
            "String" => value,
            "Number" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : throw new InvalidOperationException("Ответ не является конечным числом."),
            "Boolean" => bool.TryParse(value, out var b) ? b : value == "1" ? true : value == "0" ? false : throw new InvalidOperationException("Ответ не является логическим значением."),
            _ => throw new InvalidOperationException("Тип результата: String, Number или Boolean.")
        };
    }
    private static string ExtractJson(string input, string path)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var segments = path.Replace("[", ".").Replace("]", "").Split('.', StringSplitOptions.RemoveEmptyEntries);
        var attempts = 0;
        for (int i = 0; i < bytes.Length && attempts < 128; i++)
        {
            if (bytes[i] != (byte)'{' && bytes[i] != (byte)'[') continue;
            attempts++;
            try
            {
                var reader = new Utf8JsonReader(bytes.AsSpan(i), new JsonReaderOptions { MaxDepth = 32 });
                using var document = JsonDocument.ParseValue(ref reader);
                var current = document.RootElement; bool found = true;
                foreach (var segment in segments)
                {
                    if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var property)) current = property;
                    else if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index) && index >= 0 && index < current.GetArrayLength()) current = current[index];
                    else { found = false; break; }
                }
                if (found && current.ValueKind != JsonValueKind.Null) return current.ValueKind == JsonValueKind.String ? current.GetString()! : current.GetRawText();
                i += (int)reader.BytesConsumed - 1;
            }
            catch (JsonException) { }
        }
        throw new InvalidOperationException("В ответе нет JSON-поля: " + path);
    }
}
