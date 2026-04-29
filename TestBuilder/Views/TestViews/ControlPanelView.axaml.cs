using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using TestBuilder.ViewModels;

namespace TestBuilder.Views.TestViews;

public partial class ControlPanelView : UserControl
{
    public ControlPanelView()
    {
        InitializeComponent();
    }

    public void OnClearGraph(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;

        // Ищем GraphEditorView в визуальном дереве вверх от текущего контрола
        var editorView = this.FindAncestorOfType<Control>()?.GetVisualDescendants()
            .OfType<GraphEditorView>()
            .FirstOrDefault();

        editorView?.SelectAllNodes();

        Dispatcher.UIThread.Post(() =>
        {
            vm.DeleteSelectedNodesCommand.Execute(null);
        }, DispatcherPriority.Background);
    }
}