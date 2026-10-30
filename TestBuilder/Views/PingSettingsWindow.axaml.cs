using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TestBuilder.Models;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class PingSettingsWindow : Window
{
    public PingSettingsWindow() : this(new PingMonitorSettings()) { }
    public PingSettingsWindow(PingMonitorSettings source)
    { InitializeComponent(); DataContext = new PingSettingsViewModel(source); }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    private void OnApply(object? sender, RoutedEventArgs e)
    {
        var vm = (PingSettingsViewModel)DataContext!;
        try { Close(vm.BuildResult()); }
        catch (Exception ex) { vm.Error = ex.Message; }
    }
}
