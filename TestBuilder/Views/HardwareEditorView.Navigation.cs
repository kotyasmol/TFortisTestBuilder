using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TestBuilder.Views;

public sealed partial class HardwareEditorView
{
    private readonly ContentControl _pageHost = new() { Name = "HardwarePageHost" };
    private Control _mainPage = null!;
    private Action? _cancelInline;
    public event Action? Finished;

    public void Finish()
    {
        _cancelInline?.Invoke();
        Finished?.Invoke();
    }

    private Task<T?> EditInlineAsync<T>(InlineEquipmentEditor<T> editor, string title) where T : class
    {
        if (_cancelInline != null) return Task.FromResult<T?>(null);
        var completion = new TaskCompletionSource<T?>();
        var page = new DockPanel { Name = "HardwareInlinePage", Margin = new Thickness(20) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var back = new Button { Name = "BackFromHardwareForm", Content = "Назад", Margin = new Thickness(0, 0, 16, 0) };
        back.Click += (_, _) => editor.Cancel(); DockPanel.SetDock(back, Dock.Left); header.Children.Add(back);
        header.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = Avalonia.Media.FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        DockPanel.SetDock(header, Dock.Top); page.Children.Add(header); page.Children.Add(editor);
        void Complete(T? result)
        {
            editor.Completed -= Complete;
            _cancelInline = null;
            _pageHost.Content = _mainPage;
            completion.TrySetResult(result);
        }
        editor.Completed += Complete;
        _cancelInline = editor.Cancel;
        _pageHost.Content = page;
        return completion.Task;
    }
}
