using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models;

public sealed class ConfiguredSlaveModel : SlaveModelBase
{
    private readonly string _name;
    public override string DeviceType => _name;
    public ushort? StandSerialNumberAddress { get; }
    public ConfiguredSlaveModel(DeviceDefinition definition, IModbusService modbus) : base(definition.SlaveId, modbus)
    {
        _name = definition.Name;
        StandSerialNumberAddress = definition.StandSerialNumberAddress;
        foreach (var reg in definition.Registers)
            RegisterItems.Add(new RegisterItem { Address = reg.Address, Area = reg.Area,
                Name = TestBuilder.Presentation.RussianUiText.Display(reg.Name) + (string.IsNullOrWhiteSpace(reg.Unit) ? "" : $", {reg.Unit}"),
                IsReadOnly = !reg.Writable, Category = reg.Category });
    }
    public override Task PollAsync() => PollAsync(default);
    public override async Task PollAsync(System.Threading.CancellationToken token)
    {
        // Existing background polling, with no additional graph reading node.
        foreach (var area in RegisterItems.GroupBy(r => r.Area))
        {
            var ordered = area.OrderBy(r => r.Address).ToArray();
            for (int i = 0; i < ordered.Length;)
            {
                var end = i + 1;
                while (end < ordered.Length && end - i < 125 && ordered[end].Address == ordered[end - 1].Address + 1) end++;
                token.ThrowIfCancellationRequested();
                var values = await Modbus.ReadAreaAsync(SlaveId, (ushort)ordered[i].Address, (ushort)(end - i), area.Key, token);
                if (values.Length != end - i) throw new InvalidOperationException("Неполный ответ Modbus.");
                var offset = i;
                await Dispatcher.UIThread.InvokeAsync(() => {
                    // A profile switch can cancel this poll while publication is queued.
                    // Do not throw from the UI callback: cancellation belongs to the
                    // awaiting poll, where RegisterMonitor handles it normally.
                    if (token.IsCancellationRequested) return;
                    for (int j = 0; j < values.Length; j++) ordered[offset + j].Value = values[j];
                });
                token.ThrowIfCancellationRequested();
                i = end;
            }
        }
    }
}
