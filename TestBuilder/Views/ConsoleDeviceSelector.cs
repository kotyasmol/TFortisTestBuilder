using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using TestBuilder.Domain.Modbus;

namespace TestBuilder.Views;

/// <summary>Keep the persisted ID while Avalonia attaches or replaces the list of devices.</summary>
public sealed class ConsoleDeviceSelector : ComboBox
{
    public static readonly StyledProperty<IEnumerable<ConsoleDeviceDefinition>?> DevicesProperty =
        AvaloniaProperty.Register<ConsoleDeviceSelector, IEnumerable<ConsoleDeviceDefinition>?>(nameof(Devices));
    public static readonly StyledProperty<string> DeviceIdProperty =
        AvaloniaProperty.Register<ConsoleDeviceSelector, string>(nameof(DeviceId), "", defaultBindingMode: BindingMode.TwoWay);
    public IEnumerable<ConsoleDeviceDefinition>? Devices { get => GetValue(DevicesProperty); set => SetValue(DevicesProperty, value); }
    public string DeviceId { get => GetValue(DeviceIdProperty); set => SetValue(DeviceIdProperty, value); }
    protected override Type StyleKeyOverride => typeof(ComboBox);
    private bool _syncing;

    public ConsoleDeviceSelector()
    {
        SelectionChanged += (_, _) =>
        {
            if (!_syncing && SelectedItem is ConsoleDeviceDefinition device)
                SetCurrentValue(DeviceIdProperty, device.Id);
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != DevicesProperty && change.Property != DeviceIdProperty) return;
        _syncing = true;
        try
        {
            if (change.Property == DevicesProperty) ItemsSource = Devices?.ToArray();
            SelectedItem = ItemsSource?.Cast<ConsoleDeviceDefinition>().FirstOrDefault(d => d.Id == DeviceId);
        }
        finally { _syncing = false; }
    }
}
