using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Services;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels;

public partial class DataTestPairRow : ObservableObject
{
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string firstIp = "";
    [ObservableProperty] private string secondIp = "";
    [ObservableProperty] private int mbps = 100;
}

// A detached draft: cancelling the window never changes the node.
public sealed class DataTestPairEditorViewModel
{
    public ObservableCollection<DataTestPairRow> Rows { get; } = new();
    public string[] Addresses => AppSettings.Instance.NetworkPlan.Addresses.Select(a => a.Ip)
        .Concat(Rows.SelectMany(r => new[] { r.FirstIp, r.SecondIp })).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
    public string AddressLabel(string ip)
    {
        var port = AppSettings.Instance.NetworkPlan.Addresses.FirstOrDefault(a => a.Ip == ip);
        return string.IsNullOrWhiteSpace(port?.Name) ? ip : $"{port.Name} · {ip}";
    }
    public DataTestPairEditorViewModel(RunDataTestNodeViewModel node)
    {
        Read(node.PortsText, true, node.TargetBandwidthMbps, node.AllowGigabit);
        Read(node.DisabledPortsText, false, node.TargetBandwidthMbps, true);
    }
    private void Read(string text, bool enabled, int fallback, bool allowGigabit)
    {
        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(',').Select(s => s.Trim()).ToArray();
            if (fields.Length is < 3 or > 4 || (fields.Length == 4 && !int.TryParse(fields[3], out _)))
                throw new InvalidOperationException($"Не удалось прочитать пару: {line}");
            Rows.Add(new DataTestPairRow { Enabled = enabled, Name = fields[0], FirstIp = fields[1], SecondIp = fields[2],
                Mbps = TestBuilder.Domain.Steps.RunDataTestStep.NormalizeBandwidth(fields.Length == 4 ? int.Parse(fields[3]) : fallback, allowGigabit) });
        }
    }
    public void SetAllSpeeds(int mbps) { foreach (var row in Rows) row.Mbps = mbps; }
    public void Apply(RunDataTestNodeViewModel node)
    {
        if (!Rows.Any(r => r.Enabled)) throw new InvalidOperationException("Включите хотя бы одну пару.");
        foreach (var row in Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || row.Name.Any(c => c == ',' || char.IsControl(c)))
                throw new InvalidOperationException("Задайте название каждой пары без запятых и переводов строки.");
            foreach (var ip in new[] { row.FirstIp, row.SecondIp })
                if (!IPAddress.TryParse(ip, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork || parsed.ToString() != ip)
                    throw new InvalidOperationException($"Пара «{row.Name}»: выберите два адреса IPv4.");
            if (row.FirstIp == row.SecondIp) throw new InvalidOperationException($"Пара «{row.Name}»: выберите разные карты.");
            if (row.Mbps is < 1 or > 1000) throw new InvalidOperationException("Скорость теста: от 1 до 1000 Мбит/с.");
        }
        if (Rows.Select(r => r.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Rows.Count)
            throw new InvalidOperationException("Названия пар должны различаться.");
        string Format(bool enabled) => string.Join(Environment.NewLine, Rows.Where(r => r.Enabled == enabled)
            .Select(r => $"{r.Name.Trim()},{r.FirstIp},{r.SecondIp},{r.Mbps}"));
        var active = Format(true); var disabled = Format(false);
        node.AllowGigabit = Rows.Any(r => r.Enabled && r.Mbps > 100);
        node.PortsText = active;
        node.DisabledPortsText = disabled;
    }
}
