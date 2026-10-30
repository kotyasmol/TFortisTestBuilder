using System;
using System.Linq;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class ProfileActionEditorWindow : Window
{
    private readonly ModbusService _modbus = new();
    private readonly TestViewModel _on;
    private readonly TestViewModel _off;
    private readonly string _id;
    private readonly HardwareConfiguration? _hardware;
    private bool _ready;
    private readonly ProfileActionStateEditorViewModel _state;
    private readonly ProfileActionLabels _labels;

    public ProfileActionEditorWindow() : this(new ProfileActionDefinition(), null) { }

    public ProfileActionEditorWindow(ProfileActionDefinition source, HardwareConfiguration? hardware)
    {
        InitializeComponent();
        _id = source.Id; _hardware = hardware;
        _labels = source.GetLabels(); LabelsEditor.DataContext = _labels;
        _state = new(source.StateBinding, hardware, _labels); StateEditor.DataContext = _state;
        _on = CreateEditor(source.Graph);
        _off = CreateEditor(source.OffGraph);
        OnEditor.DataContext = _on; OffEditor.DataContext = _off;
        NameInput.Text = source.Name;
        ColorInput.Text = ProfileActionDefinition.IsSystemColor(source.Color) ? "Системный" : source.Color;
        DisplayInput.SelectedIndex = (int)source.Display;
        _ready = true; UpdateDisplay(); UpdateColorSelection();
        Closed += (_, _) => { OnEditor.Dispose(); OffEditor.Dispose(); _on.Dispose(); _off.Dispose(); _modbus.Dispose(); };
    }

    private TestViewModel CreateEditor(ViewModels.Graphs.GraphWorkspaceViewModel graph)
    {
        // Detached draft: closing the window cannot change the saved action or start hardware.
        var vm = new TestViewModel(_modbus, new SlaveManager(_modbus)) { IsActionGraphEditor = true };
        vm.ConfirmClearGraphAsync = () => new ClearGraphDialog().ShowDialog<bool>(this);
        var dto = GraphSerializer.ExportActionGraph(graph); dto.Hardware = _hardware;
        try { GraphSerializer.Deserialize(JsonSerializer.Serialize(dto), vm); return vm; }
        catch { vm.Dispose(); throw; }
    }
    private void OnDisplayChanged(object? sender, SelectionChangedEventArgs e) { if (_ready) UpdateDisplay(); }
    private void UpdateDisplay()
    {
        var toggle = DisplayInput.SelectedIndex == (int)ProfileActionDisplay.Toggle;
        OnTab.Header = toggle ? "Включить" : "Действие";
        OffTab.Header = "Выключить";
        OffTab.IsVisible = toggle; ToggleHint.IsVisible = toggle;
        if (!toggle) GraphTabs.SelectedIndex = 0;
    }
    private void OnPickColor(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color })
            ColorInput.Text = ProfileActionDefinition.IsSystemColor(color) ? "Системный" : color;
    }
    private string SelectedColor => string.Equals(ColorInput.Text?.Trim(), "Системный", StringComparison.OrdinalIgnoreCase)
        ? ProfileActionDefinition.SystemColor : ColorInput.Text?.Trim() ?? "";
    private void OnColorChanged(object? sender, TextChangedEventArgs e) { if (_ready) UpdateColorSelection(); }
    private void UpdateColorSelection()
    {
        var system = ProfileActionDefinition.IsSystemColor(SelectedColor);
        var valid = Color.TryParse(SelectedColor, out var current);
        foreach (var swatch in ColorPalette.Children.OfType<Button>())
        {
            var selected = swatch.Tag is string value && (ProfileActionDefinition.IsSystemColor(value)
                ? system : valid && Color.TryParse(value, out var color) && color == current);
            swatch.Classes.Set("selected", selected);
            var name = ToolTip.GetTip(swatch)?.ToString() ?? "Цвет";
            AutomationProperties.SetName(swatch, selected ? $"{name} — выбран" : name);
        }
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    internal ProfileActionDefinition BuildResult()
    {
        var result = new ProfileActionDefinition {
            Id = _id, Name = NameInput.Text?.Trim() ?? "", Color = SelectedColor,
            Display = (ProfileActionDisplay)DisplayInput.SelectedIndex,
            Labels = _labels.Copy(), StateBinding = _state.BuildResult(), Graph = _on.RootGraph, OffGraph = _off.RootGraph
        };
        result.Validate();
        using var compiler = new GraphCompiler(_modbus, _on.TestingLogger) { ProfileHardware = _hardware };
        compiler.Compile(result.Graph);
        if (result.Display == ProfileActionDisplay.Toggle) compiler.Compile(result.OffGraph);
        // The editor owns its nodes; the profile receives a deep copy.
        result.Graph = GraphSerializer.ImportActionGraph(GraphSerializer.ExportActionGraph(result.Graph), _hardware);
        result.OffGraph = GraphSerializer.ImportActionGraph(GraphSerializer.ExportActionGraph(result.OffGraph), _hardware);
        return result;
    }
    private void OnApply(object? sender, RoutedEventArgs e)
    {
        try { Close(BuildResult()); }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
