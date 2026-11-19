using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestBuilder.ViewModels;

public partial class NetworkSetupViewModel
{
    public ObservableCollection<NetworkSetupRowViewModel> VisibleRows { get; } = new();
    public string[] RowFilters { get; } = { "Все порты", "Карта не выбрана", "Нет линка" };
    [ObservableProperty] private string rowSearch = "";
    [ObservableProperty] private int rowFilterIndex;
    public string VisibleRowsSummary => $"Показано портов: {VisibleRows.Count} из {Rows.Count}";
    partial void OnRowSearchChanged(string value) => UpdateVisibleRows();
    partial void OnRowFilterIndexChanged(int value) => UpdateVisibleRows();

    private void UpdateVisibleRows()
    {
        var query = RowSearch?.Trim() ?? "";
        var desired = Rows.Where(row =>
            (RowFilterIndex != 1 || row.SelectedAdapter == null) &&
            (RowFilterIndex != 2 || row.SelectedAdapter is { } card && !card.Status.Equals("Up", StringComparison.OrdinalIgnoreCase)) &&
            (query.Length == 0 || string.Join(" ", row.Port, row.Ip, row.Role, row.TestAssignment,
                row.SelectedAdapter?.Name, row.SelectedAdapter?.Mac, row.SelectedAdapter?.Ipv4)
                .Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        var wanted = desired.ToHashSet();
        foreach (var removed in VisibleRows.Where(row => !wanted.Contains(row)).ToArray()) VisibleRows.Remove(removed);
        for (var i = 0; i < desired.Length; i++)
        {
            if (i < VisibleRows.Count && ReferenceEquals(VisibleRows[i], desired[i])) continue;
            var oldIndex = VisibleRows.IndexOf(desired[i]);
            if (oldIndex >= 0) VisibleRows.Move(oldIndex, i); else VisibleRows.Insert(i, desired[i]);
        }
        OnPropertyChanged(nameof(VisibleRowsSummary));
    }
}
