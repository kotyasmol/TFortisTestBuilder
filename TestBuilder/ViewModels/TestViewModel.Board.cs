using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.Graphs;

namespace TestBuilder.ViewModels;
public partial class TestViewModel
{
    public string[] ProductFilters { get; } = { "Все изделия", "Коммутаторы", "Платы" };
    [ObservableProperty] private string productFilter = "Все изделия";
    partial void OnProductFilterChanged(string value)
    {
        RefreshProfiles();
    }
    public string[] ProductKinds { get; } = { "Коммутатор", "Плата" };
    public string ProfileProductKind
    {
        get => IsBoardProfile ? "Плата" : "Коммутатор";
        set
        {
            if (!CanEditGraph || value is not ("Плата" or "Коммутатор")) return;
            var kind = value == "Плата" ? "Board" : "Switch";
            var previous = RootGraph.ProductKind;
            if (kind == previous) return;
            void Set(string type) { RootGraph.ProductKind = type; NotifyProfileMetadataChanged(); }
            _history.Execute(RootGraph, new ProfileMetadataCommand(() => Set(kind), () => Set(previous)));
        }
    }
    private bool MatchesProductFilter(GraphProfile p) => ProductFilter == "Все изделия" ||
        (ProductFilter == "Платы" ? p.ProductKind == "Board" : p.ProductKind != "Board");
    public bool IsBoardProfile => RootGraph.ProductKind == "Board";
    public bool IsAutonomousStand => IsBoardProfile;
    public bool HasConsoleSettings => IsBoardProfile || RootGraph.Hardware?.ConsoleDevices.Count > 0;
    public bool CanEditHardware => CanEditGraph && !IsConnected && !IsConnecting;
    public IEnumerable<ConsoleDeviceDefinition> ConsoleDeviceChoices =>
        new[] { new ConsoleDeviceDefinition { Id = "", Name = "Порт из ноды / старых настроек" } }
            .Concat(RootGraph.Hardware?.ConsoleDevices ?? Enumerable.Empty<ConsoleDeviceDefinition>());
    public event Action? BoardChanged;
    private readonly Dictionary<NodeViewModel, string> _boardNodeStates = new();
    public void NotifyProfileMetadataChanged()
    {
        _boardNodeStates.Clear();
        OnPropertyChanged(nameof(IsBoardProfile));
        OnPropertyChanged(nameof(ProfileProductKind));
        OnPropertyChanged(nameof(HasConsoleSettings));
        OnPropertyChanged(nameof(IsAutonomousStand));
        RefreshStandIdentity();
        RefreshStationAvailability();
        OnPropertyChanged(nameof(CanEditHardware));
        OnPropertyChanged(nameof(ConsoleDeviceChoices));
        BoardChanged?.Invoke();
    }
    public static T CopyDefinition<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    public void ApplyHardware(HardwareConfiguration? hardware)
    {
        if (!CanEditHardware) throw new InvalidOperationException("Остановите тест и отключите стенд перед изменением оборудования.");
        hardware?.Validate();
        GraphCompiler.ValidateConnections(RootGraph, hardware);
        var old = RootGraph.Hardware;
        _history.Execute(RootGraph, new ProfileMetadataCommand(
            () => { RootGraph.Hardware = hardware; NotifyProfileMetadataChanged(); },
            () => { RootGraph.Hardware = old; NotifyProfileMetadataChanged(); }));
    }
    public void ApplyBoard(BoardDesign board, bool isBoard)
    {
        if (!CanEditGraph) throw new InvalidOperationException("Остановите проверку перед редактированием платы.");
        board.Validate();
        var old = RootGraph.Board; var kind = RootGraph.ProductKind;
        _history.Execute(RootGraph, new ProfileMetadataCommand(
            () => { RootGraph.Board = board; RootGraph.ProductKind = isBoard ? "Board" : "Switch"; NotifyProfileMetadataChanged(); },
            () => { RootGraph.Board = old; RootGraph.ProductKind = kind; NotifyProfileMetadataChanged(); }));
    }
    private static IEnumerable<NodeViewModel> BoardNodes(GraphWorkspaceViewModel graph)
    {
        foreach (var n in graph.Nodes)
        {
            yield return n;
            if (n is ICompositeNodeViewModel c) foreach (var child in BoardNodes(c.BodyGraph)) yield return child;
        }
    }
    public string GetBoardRegionState(string id)
    {
        var nodes = BoardNodes(RootGraph).Where(n => n.BoardElements.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(id)).ToArray();
        if (nodes.Length == 0) return "Не привязан";
        var states = nodes.Select(n => _boardNodeStates.GetValueOrDefault(n, "Не проверен")).ToArray();
        if (states.Contains("Ошибка")) return "Ошибка";
        if (states.Contains("Проверяется")) return "Проверяется";
        return states.All(s => s == "Годен") ? "Годен" : "Не проверен";
    }
    private void UpdateBoardState(NodeViewModel node, string state)
    {
        if (string.IsNullOrWhiteSpace(node.BoardElements)) return;
        _boardNodeStates[node] = state;
        BoardChanged?.Invoke();
    }
}
