using Avalonia;
using Avalonia.Controls;
using TestBuilder.Services;

namespace TestBuilder.Views;

public sealed class NetworkPortBatchView : WorkspacePage
{
    internal NetworkPortBatchView(BenchNetworkPlan source)
    {
        Title = "Добавить порты в план";

        var editor = new NetworkPortBatchEditor(source, result => Close(result)) { Margin = new Thickness(20) };
        editor.Cancelled += () => Close(null); Content = editor;
    }
}
