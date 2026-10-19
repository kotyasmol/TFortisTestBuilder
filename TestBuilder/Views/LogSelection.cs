using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TestBuilder.Views;

internal static class LogSelection
{
    public static void Enable(ListBox list)
    {
        list.SelectionMode = SelectionMode.Multiple;
        list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    internal static string GetCopyText(ListBox list)
    {
        // Keep journal order even when rows were selected in a different order.
        var indices = list.Selection.SelectedIndexes.OrderBy(i => i).ToArray();
        var entries = indices.Length == 0 ? list.Items.Cast<object?>()
            : indices.Select(i => list.Items[i]);
        return string.Join(Environment.NewLine, entries);
    }

    public static async Task CopyAsync(ListBox list)
    {
        var clipboard = TopLevel.GetTopLevel(list)?.Clipboard;
        if (clipboard != null && list.Items.Count > 0)
            await clipboard.SetTextAsync(GetCopyText(list));
    }

    private static async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox list || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0)
            return;
        if (e.Key == Key.A)
        {
            list.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.C)
        {
            e.Handled = true;
            await CopyAsync(list);
        }
    }
}
