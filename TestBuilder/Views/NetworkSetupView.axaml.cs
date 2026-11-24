using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Markup.Xaml.MarkupExtensions;
using System;
using System.Text.Json;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class NetworkSetupView : UserControl
{
    public NetworkSetupView()
    {
        InitializeComponent();
        NetworkDiagnosticsTabs.SelectedIndex = OperatingSystem.IsWindows() ? 0 : 1;
        DataContextChanged += (_, _) => ClosePairEditor();
    }

    private void OnPresetAction(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() => NetworkPresetsButton.Flyout?.Hide());

    private void OnDiagnosticsToggled(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle)
            toggle.Content = toggle.IsChecked == true ? "Скрыть диагностику" : "Диагностика";
    }

    private void ClosePairEditor()
    {
        NetworkPairEditorHost.Content = null;
        NetworkPairEditorHost.IsVisible = false;
    }

    private void OnAddNetworkPairs(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NetworkSetupViewModel { CanEdit: true } vm || NetworkPairEditorHost.Content != null) return;
        try
        {
            var source = TestViewModel.CopyDefinition(AppSettings.Instance.NetworkPlan);
            var original = JsonSerializer.Serialize(source);
            var editor = new NetworkPortBatchEditor(source, result =>
            {
                if (!ReferenceEquals(DataContext, vm) || original != JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan))
                    throw new InvalidOperationException("План изменился во время добавления. Отмените форму и откройте её заново.");
                vm.SaveAddressPlan(result);
                vm.RowSearch = ""; vm.RowFilterIndex = 0;
                vm.Status = $"Добавлено пар: {(result.Addresses.Count - source.Addresses.Count) / 2}. Выберите карты для новых портов и сохраните привязки.";
                ClosePairEditor();
                if (vm.Rows.Count > source.Addresses.Count)
                {
                    var first = vm.Rows[source.Addresses.Count];
                    NetworkPlanAdaptersGrid.SelectedItem = first;
                    Dispatcher.UIThread.Post(() =>
                    {
                        NetworkPlanAdaptersGrid.ScrollIntoView(first, NetworkPlanAdaptersGrid.Columns[0]);
                        NetworkPlanAdaptersGrid.BringIntoView();
                    });
                }
            }, pairCount: true, maxFormHeight: 340);
            editor.Cancelled += ClosePairEditor;
            var frame = new Border {
                BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(8), Padding = new Avalonia.Thickness(16), Child = editor };
            frame.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("BorderBrush"));
            NetworkPairEditorHost.Content = frame;
            NetworkPairEditorHost.IsVisible = true; NetworkPlanSection.IsExpanded = true;
            Dispatcher.UIThread.Post(() => editor.BringIntoView());
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }

    private async void OnConfigureAdapter(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NetworkSetupViewModel { CanEdit: true } vm || sender is not Button { DataContext: ComputerNetworkAdapterViewModel card } ||
            TopLevel.GetTopLevel(this) is not Window owner) return;
        try { await new NetworkAdapterSettingsWindow(vm.CreateAdapterSettings(card.Adapter.Id)).ShowDialog(owner); }
        catch (Exception ex) { vm.Status = ex.Message; }
    }

    private void OnSystemNetworkSettings(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NetworkSetupViewModel { CanEdit: true } vm) return;
        try { NetworkSetupViewModel.OpenSystemNetworkSettings(); }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
}
