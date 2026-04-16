using Avalonia;
using Avalonia.Controls;
using System;
using System.Linq;
using TestBuilder.ViewModels;
using Avalonia.Input;
using TestBuilder.Domain.Modbus.Models;
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

    private async void OnDataGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;
        if (dataGrid.DataContext is not SlaveModelBase slave) return;
        if (slave.SelectedRegister is not RegisterItem register) return;
        if (register.IsReadOnly) return;

        var dialog = new WriteRegisterDialog(slave, register);
        await dialog.ShowDialog(TopLevel.GetTopLevel(this) as Window ?? throw new Exception("No window"));
    }

    public ModbusMonitoringViewModel? ViewModel
    {
        get => DataContext as ModbusMonitoringViewModel;
        set => DataContext = value;
    }
}