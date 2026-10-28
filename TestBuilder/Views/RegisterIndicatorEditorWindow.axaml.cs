using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class RegisterIndicatorEditorWindow : Window
{
    public RegisterIndicatorEditorWindow() : this(new RegisterIndicatorDefinition(), null) { }
    public RegisterIndicatorEditorWindow(RegisterIndicatorDefinition source, HardwareConfiguration? hardware)
    { InitializeComponent(); DataContext = new RegisterIndicatorEditorViewModel(source, hardware); }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    private void OnChooseRegister(object? sender, RoutedEventArgs e)
    {
        var input = this.FindControl<AutoCompleteBox>("RegisterInput")!;
        input.Focus();
        // The arrow shows all registers, preserving the current selection.
        input.FilterMode = AutoCompleteFilterMode.None;
        input.IsDropDownOpen = true;
    }
    private void OnRegisterTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is AutoCompleteBox input) input.FilterMode = AutoCompleteFilterMode.Contains;
    }
    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RegisterIndicatorEditorViewModel vm) return;
        try { Close(BuildResult()); }
        catch (Exception ex) { vm.Error = ex.Message; }
    }
    internal Models.RegisterIndicatorDefinition BuildResult()
    {
        var vm = (RegisterIndicatorEditorViewModel)DataContext!;
        var input = this.FindControl<AutoCompleteBox>("RegisterInput")!;
        // AutoCompleteBox queues text updates. Validate the visible text before Apply.
        var text = input.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Text ?? input.Text;
        if (vm.SelectedRegister == null || text != vm.SelectedRegister.ToString())
            throw new InvalidOperationException("Выберите регистр из списка.");
        return vm.BuildResult();
    }
}
