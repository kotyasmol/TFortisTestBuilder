using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;

namespace TestBuilder.Domain.Modbus;

public sealed class DiscoverySettings
{
    public int BaudRate { get; set; } = 9600;
    public Parity Parity { get; set; }
    public int DataBits { get; set; } = 8;
    public StopBits StopBits { get; set; } = StopBits.One;
    public string SlaveAddresses { get; set; } = "1,3,5,7,9,11,13,15,17,19,21,23,25,27,29,31,33,35";
    public int DelayMs { get; set; } = 150;
    // Null migrates existing installations to the built-in catalogue; an empty selection is invalid.
    public List<string>? TemplateIds { get; set; }
    public byte[] GetAddresses()
    {
        var result = SlaveAddresses.Split(',').Select(s =>
            byte.TryParse(s.Trim(), out var id) && id is >= 1 and <= 247 ? id :
            throw new InvalidOperationException("Адреса поиска: числа 1–247 через запятую.")).ToArray();
        if (result.Distinct().Count() != result.Length) throw new InvalidOperationException("Адреса поиска повторяются.");
        return result;
    }
    public void Validate()
    {
        _ = GetAddresses();
        if (BaudRate is < 300 or > 1000000 || DataBits is < 5 or > 8 || !Enum.IsDefined(Parity) ||
            !Enum.IsDefined(StopBits) || StopBits == StopBits.None || DelayMs is < 0 or > 60000)
            throw new InvalidOperationException("Проверьте параметры линии и паузу поиска (0–60000 мс).");
        if (TemplateIds is { Count: 0 }) throw new InvalidOperationException("Выберите хотя бы один тип оборудования для поиска.");
    }
}
