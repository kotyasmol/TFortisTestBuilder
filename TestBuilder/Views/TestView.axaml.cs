using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class TestView : UserControl, IDisposable
{
    public TestView()
    {
        InitializeComponent();

        var topLevel = TopLevel.GetTopLevel(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            topLevel.KeyDown += OnWindowKeyDown;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            topLevel.KeyDown -= OnWindowKeyDown;
        if (DataContext is TestViewModel vm)
            vm.PendingConnection.Reset();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;
        if (e.Key == Key.Delete)
        {
            if (vm.SelectedNodes.Count > 0)
                vm.DeleteSelectedNodesCommand.Execute(null);
            else if (vm.SelectedConnection != null)
                vm.DeleteSelectedConnection();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && vm.SelectedConnection != null)
        {
            vm.SelectConnection(null);
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        LogPanel.Dispose();
        GraphEditor.Dispose();
    }
}
