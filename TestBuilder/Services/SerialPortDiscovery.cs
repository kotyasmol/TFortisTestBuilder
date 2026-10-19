using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using TestBuilder.Domain.Modbus;

namespace TestBuilder.Services;

internal sealed record SerialPortInfo(string PortName, string Description = "", string VendorId = "", string ProductId = "");

internal interface ISerialPortCatalog
{
    Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(CancellationToken token);
}

internal static class ConsolePortDiscovery
{
    internal static bool Matches(ConsoleDeviceDefinition device, SerialPortInfo port) =>
        (!string.IsNullOrWhiteSpace(device.PortDescriptionContains) &&
         port.Description.Contains(device.PortDescriptionContains.Trim(), StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrEmpty(device.UsbVendorId) && !string.IsNullOrEmpty(device.UsbProductId) &&
         string.Equals(device.UsbVendorId, port.VendorId, StringComparison.OrdinalIgnoreCase) &&
         string.Equals(device.UsbProductId, port.ProductId, StringComparison.OrdinalIgnoreCase));

    internal static string Resolve(ConsoleDeviceDefinition device, IEnumerable<SerialPortInfo> ports, IEnumerable<string> excluded)
    {
        device.Validate(requirePort: true);
        var exclusions = excluded.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(device.PortName))
        {
            var explicitPort = device.PortName.Trim();
            if (exclusions.Contains(explicitPort))
                throw new InvalidOperationException($"Порт {explicitPort} уже используется другим устройством. Проверьте оборудование профиля.");
            return explicitPort;
        }
        var matches = ports.Where(p => !exclusions.Contains(p.PortName) && Matches(device, p))
            .Select(p => p.PortName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return matches.Length switch
        {
            0 => throw new TimeoutException($"«{device.Name}»: подходящий USB-UART ещё не найден. Проверьте питание и подключение платы."),
            1 => matches[0],
            _ => throw new InvalidOperationException($"«{device.Name}»: найдено несколько подходящих портов ({string.Join(", ", matches)}). Выберите нужный в «Оборудование → Консоли»." )
        };
    }
}

/// <summary>Read OS descriptors only. Discovery never opens ports or sends probe commands.</summary>
internal sealed class SystemSerialPortCatalog : ISerialPortCatalog
{
    public async Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows()) return await Task.Run(() =>
            OperatingSystem.IsWindows() ? ReadWindows(token) : Array.Empty<SerialPortInfo>(), token);
        if (OperatingSystem.IsMacOS())
        {
            using var process = new Process { StartInfo = new ProcessStartInfo("/usr/sbin/ioreg")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
            foreach (var argument in new[] { "-a", "-r", "-c", "IOUSBHostDevice" }) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var xml = await output;
                await error;
                if (process.ExitCode != 0) throw new IOException("Не удалось прочитать список USB-устройств.");
                return ParseMacPorts(xml);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException("Не удалось получить список USB-устройств за отведённое время."); }
            finally { if (!process.HasExited) process.Kill(); }
        }
        if (OperatingSystem.IsLinux()) return await Task.Run(() => ReadLinux(token), token);
        return SerialPort.GetPortNames().Select(p => new SerialPortInfo(p, p)).ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<SerialPortInfo> ReadWindows(CancellationToken token)
    {
        var available = SerialPort.GetPortNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<SerialPortInfo>();
        using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usb == null) return found;
        foreach (var hardwareId in usb.GetSubKeyNames())
        {
            token.ThrowIfCancellationRequested();
            using var hardware = usb.OpenSubKey(hardwareId);
            if (hardware == null) continue;
            foreach (var instance in hardware.GetSubKeyNames())
            {
                token.ThrowIfCancellationRequested();
                using var device = hardware.OpenSubKey(instance);
                using var parameters = device?.OpenSubKey("Device Parameters");
                if (parameters?.GetValue("PortName") is not string port || !available.Contains(port)) continue;
                found.Add(FromWindowsDescriptor(port, device?.GetValue("FriendlyName") as string ?? "", hardwareId));
            }
        }
        return found;
    }

    internal static SerialPortInfo FromWindowsDescriptor(string port, string description, string hardwareId)
    {
        string Id(string name) => Regex.Match(hardwareId, name + "_([0-9a-f]{4})", RegexOptions.IgnoreCase).Groups[1].Value;
        return new(port, description, Id("VID"), Id("PID"));
    }

    internal static IReadOnlyList<SerialPortInfo> ParseMacPorts(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        var document = XDocument.Load(reader);
        var found = new List<SerialPortInfo>();
        static XElement? Value(XElement dict, string key) => dict.Elements("key").FirstOrDefault(k => k.Value == key)?.ElementsAfterSelf().FirstOrDefault();
        static string Id(XElement dict, string key, string fallback) => int.TryParse(Value(dict, key)?.Value, out var value) ? value.ToString("X4") : fallback;
        void Visit(XElement dict, string vendor, string product, string description)
        {
            vendor = Id(dict, "idVendor", vendor); product = Id(dict, "idProduct", product);
            description = Value(dict, "USB Product Name")?.Value ?? description;
            var port = Value(dict, "IOCalloutDevice")?.Value;
            if (!string.IsNullOrEmpty(port)) found.Add(new(port, description, vendor, product));
            if (Value(dict, "IORegistryEntryChildren") is { } children)
                foreach (var child in children.Elements("dict")) Visit(child, vendor, product, description);
        }
        foreach (var root in document.Root?.Element("array")?.Elements("dict") ?? Enumerable.Empty<XElement>()) Visit(root, "", "", "");
        return found;
    }

    private static IReadOnlyList<SerialPortInfo> ReadLinux(CancellationToken token)
    {
        var found = new List<SerialPortInfo>();
        foreach (var port in SerialPort.GetPortNames())
        {
            token.ThrowIfCancellationRequested();
            var link = new DirectoryInfo("/sys/class/tty/" + Path.GetFileName(port) + "/device");
            var directory = link.Exists ? link.ResolveLinkTarget(true) as DirectoryInfo : null;
            while (directory != null)
            {
                token.ThrowIfCancellationRequested();
                var vendor = Path.Combine(directory.FullName, "idVendor");
                var product = Path.Combine(directory.FullName, "idProduct");
                if (File.Exists(vendor) && File.Exists(product))
                {
                    var name = Path.Combine(directory.FullName, "product");
                    found.Add(new(port, File.Exists(name) ? File.ReadAllText(name).Trim() : "", File.ReadAllText(vendor).Trim(), File.ReadAllText(product).Trim()));
                    break;
                }
                directory = directory.Parent;
            }
        }
        return found;
    }
}
