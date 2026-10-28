using System;
using Avalonia;
using System.Linq;
using TestBuilder.Services;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Models;

public enum ProfileActionDisplay { Button, Toggle, Progress }

public sealed class ProfileActionDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Новое действие";
    public string Color { get; set; } = "#2563EB";
    public ProfileActionDisplay Display { get; set; }
    public GraphWorkspaceViewModel Graph { get; set; } = EmptyGraph("Действие");
    public GraphWorkspaceViewModel OffGraph { get; set; } = EmptyGraph("Выключить");

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100) throw new InvalidOperationException("Некорректный ID кнопки.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Name.Any(char.IsControl)) throw new InvalidOperationException("Введите название кнопки до 100 символов.");
        if (!Avalonia.Media.Color.TryParse(Color, out _) || !Enum.IsDefined(Display))
            throw new InvalidOperationException("Некорректный цвет или вид кнопки.");
        if (Graph == null || OffGraph == null) throw new InvalidOperationException("Не задан граф действия.");
        if (Graph.QuickActions.Count != 0 || OffGraph.QuickActions.Count != 0)
            throw new InvalidOperationException("Кнопки не могут содержать другие кнопки.");
    }
    public static GraphWorkspaceViewModel EmptyGraph(string title)
    {
        var graph = new GraphWorkspaceViewModel { Title = title };
        var start = new StartNodeViewModel { Location = new Point(80, 80) };
        var end = new EndNodeViewModel { Location = new Point(420, 80) };
        graph.Nodes.Add(start); graph.Nodes.Add(end);
        graph.Connections.Add(new ConnectionViewModel(start.Output[0], end.Input[0]));
        return graph;
    }
}
