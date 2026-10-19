using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodify;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.Views.TestViews;

namespace ProfileLayout;

public sealed record LayoutReport(int Graphs, int Nodes, int Moved, IReadOnlyList<string> Conflicts);

/// <summary>Offline measurement of real editor templates. Never executes a graph or connects to hardware.</summary>
public static class ProfileLayoutRunner
{
    public const double Gap = 96;
    public const double Edge = 80;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static LayoutReport Process(string path, bool write)
    {
        var source = File.ReadAllText(path);
        var json = JsonNode.Parse(source)!.AsObject();
        using var bus = new ModbusService();
        using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false, IsPaletteCollapsed = true };
        GraphSerializer.Deserialize(source, vm);
        using var view = new GraphEditorView { DataContext = vm };
        var window = new Window { Width = 1440, Height = 1000, Content = view };
        var theme = Application.Current!.RequestedThemeVariant;
        var graphs = 0; var nodes = 0; var moved = 0; var conflicts = new List<string>();
        var capturedBody = false;
        try
        {
            window.Show();
            Visit(vm.RootGraph, json, "root");
            if (write && conflicts.Count == 0 && moved > 0)
            {
                // Patch the original JSON tree: preserve unknown fields, IDs, connections, dates and parameters.
                var temporary = Path.GetFullPath(path) + ".layout-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllText(temporary, json.ToJsonString(JsonOptions) + "\n");
                    File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return new LayoutReport(graphs, nodes, moved, conflicts);
        }
        finally
        {
            view.DataContext = null; window.Content = null; window.Close();
            Application.Current.RequestedThemeVariant = theme;
            Layout(window);
        }

        void Visit(GraphWorkspaceViewModel graph, JsonObject document, string graphPath)
        {
            var rawNodes = Property(document, "nodes")!.AsArray();
            if (rawNodes.Count != graph.Nodes.Count) throw new InvalidOperationException($"{graphPath}: node count changed during loading");
            var sizes = new Dictionary<NodeViewModel, Size>();
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current!.RequestedThemeVariant = variant;
                Layout(window);
                var containers = view.FindControl<NodifyEditor>("Editor")!.GetVisualDescendants().OfType<ItemContainer>().ToArray();
                foreach (var node in graph.Nodes)
                {
                    var container = containers.Single(c => ReferenceEquals(c.DataContext, node));
                    var size = container.Bounds.Size; // Graph units, independent of viewport zoom.
                    if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
                        throw new InvalidOperationException($"{graphPath}: {node.Title} was not measured");
                    sizes.TryGetValue(node, out var old);
                    sizes[node] = new Size(Math.Max(size.Width, old.Width), Math.Max(size.Height, old.Height));
                }
            }
            var boxes = graph.Nodes.Select(n => new Rect(n.Location, sizes[n])).ToArray();
            if (write)
            {
                var positions = Arrange(boxes);
                for (var i = 0; i < graph.Nodes.Count; i++)
                {
                    if (graph.Nodes[i].Location == positions[i]) continue;
                    graph.Nodes[i].Location = positions[i];
                    Set(rawNodes[i]!.AsObject(), "x", positions[i].X);
                    Set(rawNodes[i]!.AsObject(), "y", positions[i].Y);
                    moved++;
                }
                Layout(window);
                boxes = graph.Nodes.Select(n => new Rect(n.Location, sizes[n])).ToArray();
            }
            foreach (var (a, b) in FindConflicts(boxes))
                conflicts.Add($"{graphPath}: {Property(rawNodes[a]!.AsObject(), "id")} ({graph.Nodes[a].Title}) / {Property(rawNodes[b]!.AsObject(), "id")} ({graph.Nodes[b].Title})");
            var screenshots = Environment.GetEnvironmentVariable("TFORTIS_LAYOUT_ARTIFACTS");
            if (!string.IsNullOrWhiteSpace(screenshots) && (graphPath == "root" || (!capturedBody && graph.Nodes.Count >= 5)))
            {
                view.FindControl<NodifyEditor>("Editor")!.FitToScreen(); Layout(window);
                Directory.CreateDirectory(screenshots);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(screenshots, Path.GetFileNameWithoutExtension(path) + "-" + graphPath.Replace('/', '-') + ".png"));
                if (graphPath != "root") capturedBody = true;
            }
            graphs++; nodes += graph.Nodes.Count;
            for (var i = 0; i < graph.Nodes.Count; i++)
            {
                if (graph.Nodes[i] is not ICompositeNodeViewModel composite) continue;
                var raw = rawNodes[i]!.AsObject();
                var body = Property(raw, "bodyGraph") ?? Property(raw, "body");
                if (body is not JsonObject child) throw new InvalidOperationException($"{graphPath}: missing body for {graph.Nodes[i].Title}");
                vm.OpenCompositeNodeBodyCommand.Execute(graph.Nodes[i]);
                Visit(composite.BodyGraph, child, graphPath + "/" + Property(raw, "id"));
                vm.GoBackGraphCommand.Execute(null);
            }
        }
    }

    // Keep the author's row order and left-to-right order. Each row uses measured widths;
    // the following row starts below the tallest node. Disconnected cleanup rows stay separate.
    public static Point[] Arrange(IReadOnlyList<Rect> boxes)
    {
        var result = new Point[boxes.Count];
        var rows = new List<List<int>>();
        foreach (var i in Enumerable.Range(0, boxes.Count).OrderBy(i => boxes[i].Y).ThenBy(i => boxes[i].X))
        {
            if (!double.IsFinite(boxes[i].X) || !double.IsFinite(boxes[i].Y) || boxes[i].Width <= 0 || boxes[i].Height <= 0)
                throw new ArgumentException("Invalid node bounds");
            if (rows.Count == 0 || boxes[i].Y - boxes[rows[^1][0]].Y > 1) rows.Add(new List<int>());
            rows[^1].Add(i);
        }
        var y = Edge;
        foreach (var row in rows)
        {
            var x = Edge;
            foreach (var i in row.OrderBy(i => boxes[i].X).ThenBy(i => i))
            {
                result[i] = new Point(x, y);
                x += Snap(boxes[i].Width) + Gap;
            }
            y += Snap(row.Max(i => boxes[i].Height)) + Gap;
        }
        return result;
    }

    public static IEnumerable<(int First, int Second)> FindConflicts(IReadOnlyList<Rect> boxes)
    {
        for (var i = 0; i < boxes.Count; i++)
            for (var j = i + 1; j < boxes.Count; j++)
                if (boxes[i].Inflate(Gap / 2 - .1).Intersects(boxes[j].Inflate(Gap / 2 - .1))) yield return (i, j);
    }
    private static double Snap(double value) => Math.Ceiling(value / 16) * 16;
    private static JsonNode? Property(JsonObject value, string name) => value.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    private static void Set(JsonObject value, string name, double number) => value[value.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Key ?? name] = number;
    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs();
    }
}
