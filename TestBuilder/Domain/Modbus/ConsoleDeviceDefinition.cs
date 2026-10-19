using System;
using System.ComponentModel;
using System.IO.Ports;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TestBuilder.Domain.Modbus;

/// <summary>A named UART endpoint, opened only when a console step uses it.</summary>
public sealed record ConsoleDeviceDefinition : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _name = "Консоль устройства";
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); } }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string PortName { get; set; } = "";
    public string PortDescriptionContains { get; set; } = "";
    public string UsbVendorId { get; set; } = "";
    public string UsbProductId { get; set; } = "";
    [JsonIgnore]
    public bool HasAutoDetection => !string.IsNullOrWhiteSpace(PortDescriptionContains) ||
        (!string.IsNullOrEmpty(UsbVendorId) && !string.IsNullOrEmpty(UsbProductId));
    public int BaudRate { get; set; } = 115200;
    public Parity Parity { get; set; } = Parity.None;
    public int DataBits { get; set; } = 8;
    public StopBits StopBits { get; set; } = StopBits.One;
    public string EncodingName { get; set; } = "utf-8";
    public string LineEnding { get; set; } = "\n";
    public bool DtrEnable { get; set; }
    public bool RtsEnable { get; set; }

    public void Validate(bool requirePort = false)
    {
        if (PortName == null || string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) ||
            BaudRate is < 300 or > 1000000 || DataBits is < 5 or > 8 ||
            !Enum.IsDefined(Parity) || !Enum.IsDefined(StopBits) || StopBits == StopBits.None ||
            EncodingName is not ("utf-8" or "us-ascii" or "iso-8859-1") ||
            LineEnding is not ("" or "\n" or "\r" or "\r\n"))
            throw new InvalidOperationException($"Консоль «{Name}»: проверьте имя и параметры последовательного порта.");
        if (PortDescriptionContains == null || UsbVendorId == null || UsbProductId == null ||
            (UsbVendorId.Length > 0 || UsbProductId.Length > 0) &&
            (!Regex.IsMatch(UsbVendorId, "\\A[0-9a-fA-F]{4}\\z") || !Regex.IsMatch(UsbProductId, "\\A[0-9a-fA-F]{4}\\z")))
            throw new InvalidOperationException($"Консоль «{Name}»: укажите USB VID и PID четырьмя шестнадцатеричными цифрами или оставьте оба поля пустыми.");
        if (requirePort && string.IsNullOrWhiteSpace(PortName) && !HasAutoDetection)
            throw new InvalidOperationException($"Для «{Name}» не настроен поиск USB-UART. В «Оборудование → Консоли» задайте название адаптера для автопоиска. Порт до включения платы не требуется.");
    }

    internal SerialPort CreatePort()
    {
        Validate(requirePort: true);
        if (string.IsNullOrWhiteSpace(PortName))
            throw new InvalidOperationException($"Консоль «{Name}»: сначала нужно найти порт устройства.");
        return new SerialPort(PortName.Trim(), BaudRate, Parity, DataBits, StopBits)
        {
            Encoding = Encoding.GetEncoding(EncodingName), DtrEnable = DtrEnable,
            RtsEnable = RtsEnable, ReadTimeout = 100, WriteTimeout = 1000
        };
    }

    public override string ToString() => Name;
}
