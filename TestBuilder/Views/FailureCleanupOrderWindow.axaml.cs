using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Views;

public partial class FailureCleanupOrderWindow : Window
{
    public FailureCleanupOrderWindow() : this(new FailureCleanupOrderViewModel(Array.Empty<SubtestNodeViewModel>())) { }
    public FailureCleanupOrderWindow(FailureCleanupOrderViewModel draft)
    {
        InitializeComponent();
        DataContext = draft;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    private void OnApply(object? sender, RoutedEventArgs e) => Close(((FailureCleanupOrderViewModel)DataContext!).BuildResult());
}
