using Avalonia;
using Avalonia.Controls;
using System;
using System.Linq;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class ModbusMonitoringView : UserControl
{
    public ModbusMonitoringView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private bool _initialized = false;
    private async void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ModbusMonitoringViewModel vm && !_initialized)
        {
            _initialized = true;
            await vm.ScanAndStartAsync();
        }

    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is ModbusMonitoringViewModel vm)
        {
            vm.Stop();
        }
    }

    public ModbusMonitoringViewModel? ViewModel
    {
        get => DataContext as ModbusMonitoringViewModel;
        set => DataContext = value;
    }
}