using Avalonia;
using Avalonia.Controls;
using TestBuilder.Services;

namespace TestBuilder.Views;

public sealed class NetworkPortBatchWindow : Window
{
    internal NetworkPortBatchWindow(BenchNetworkPlan source)
    {
        Title = "Добавить порты в план";
        Width = 620; Height = 630; MinWidth = 540; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var editor = new NetworkPortBatchEditor(source, result => Close(result)) { Margin = new Thickness(20) };
        editor.Cancelled += () => Close(null); Content = editor;
    }
}
