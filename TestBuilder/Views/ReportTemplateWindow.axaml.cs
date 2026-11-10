using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TestBuilder.Domain.Execution;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

internal sealed record ReportTemplateEditResult(ReportTemplate? Template);

public partial class ReportTemplateWindow : Window
{
    public ReportTemplateWindow() : this(new ReportTemplateEditorViewModel(null)) { }

    public ReportTemplateWindow(ReportTemplateEditorViewModel draft)
    {
        InitializeComponent();
        DataContext = draft;
    }

    private ReportTemplateEditorViewModel Draft => (ReportTemplateEditorViewModel)DataContext!;

    private void OnToggleTemplate(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { IsChecked: true }) DetailsTabs.SelectedItem = VariablesTab;
    }

    private void OnIncludeVisibleVariables(object? sender, RoutedEventArgs e) => Draft.SetVisibleVariablesIncluded(true);
    private void OnExcludeVisibleVariables(object? sender, RoutedEventArgs e) => Draft.SetVisibleVariablesIncluded(false);

    private void OnAddRow(object? sender, RoutedEventArgs e)
    {
        Draft.AddRow();
        ScrollToSelection();
    }

    private void OnAddTestType(object? sender, RoutedEventArgs e)
    {
        Draft.AddTestType();
        ScrollToSelection();
    }

    private void OnDeleteRow(object? sender, RoutedEventArgs e) => Draft.DeleteSelected();

    private void OnMoveUp(object? sender, RoutedEventArgs e)
    {
        Draft.MoveSelected(-1);
        ScrollToSelection();
    }

    private void OnMoveDown(object? sender, RoutedEventArgs e)
    {
        Draft.MoveSelected(1);
        ScrollToSelection();
    }

    private void ScrollToSelection()
    {
        if (Draft.SelectedRow is { } row) FieldList.ScrollIntoView(row);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        try { Close(new ReportTemplateEditResult(Draft.BuildResult())); }
        catch (InvalidOperationException error) { Draft.Error = error.Message; }
    }
}
