using Avalonia.Controls;
using TestBuilder.Domain.Modbus;
using TestBuilder.ViewModels;
using Avalonia.Threading;
using System;
using System.Collections.Specialized;

namespace TestBuilder.Views;

public partial class ModbusMonitoringView : UserControl
{
    public ModbusMonitoringView()
    {
        InitializeComponent();
    }

    public ModbusMonitoringViewModel? ViewModel
    {
        get => DataContext as ModbusMonitoringViewModel;
        set => DataContext = value;
    }
}
