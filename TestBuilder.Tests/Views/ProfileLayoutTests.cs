using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using ProfileLayout;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ProfileLayoutTests
{
    [Fact]
    public void MixedSizes_KeepRowsAndOrder_WithoutOverlaps_AndRemainStable()
    {
        var before = new[] { new Rect(100, 100, 760, 700), new Rect(510, 100, 310, 120), new Rect(100, 620, 350, 900), new Rect(510, 620, 720, 340), new Rect(100, 2100, 320, 230) };
        Assert.NotEmpty(ProfileLayoutRunner.FindConflicts(before));
        var positions = ProfileLayoutRunner.Arrange(before);
        var after = before.Select((r, i) => new Rect(positions[i], r.Size)).ToArray();
        Assert.Empty(ProfileLayoutRunner.FindConflicts(after));
        Assert.Equal(positions[0].Y, positions[1].Y);
        Assert.True(positions[1].X >= after[0].Right + ProfileLayoutRunner.Gap);
        Assert.True(positions[2].Y >= after.Take(2).Max(r => r.Bottom) + ProfileLayoutRunner.Gap);
        Assert.True(positions[4].Y >= after.Take(4).Max(r => r.Bottom) + ProfileLayoutRunner.Gap);
        Assert.Equal(positions, ProfileLayoutRunner.Arrange(after));
    }

    [Theory]
    [InlineData("RTL_SW_board.json")]
    [InlineData("RTL_POE_board.json")]
    [InlineData("RTL_LED_color_template.json")]
    [InlineData("board_manual_label.json")]
    [InlineData("board_highlight_demo.json")]
    public async Task RealTemplates_AllBodiesReflow_OnlyCoordinatesChange_AndReloadHasNoConflicts(string profile)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../profiles", profile));
            var copy = Path.Combine(Path.GetTempPath(), "layout-" + Guid.NewGuid().ToString("N") + ".json");
            var original = JsonNode.Parse(File.ReadAllText(source))!.AsObject();
            original["futureMetadata"] = "keep me";
            original["updatedAt"] = "2025-01-01T00:00:00Z";
            // Force collisions in every nested graph, including duplicate initial positions.
            MoveToOrigin(original);
            File.WriteAllText(copy, original.ToJsonString());
            try
            {
                var before = File.ReadAllText(copy);
                Assert.NotEmpty(ProfileLayoutRunner.Process(copy, false).Conflicts);
                Assert.Equal(before, File.ReadAllText(copy));
                var fixedReport = ProfileLayoutRunner.Process(copy, true);
                Assert.True(fixedReport.Moved > 0);
                Assert.Empty(fixedReport.Conflicts);
                var saved = File.ReadAllText(copy);
                var corrected = JsonNode.Parse(saved)!.AsObject();
                RemoveCoordinates(original); RemoveCoordinates(corrected);
                Assert.True(JsonNode.DeepEquals(original, corrected), "Only graph node x/y may change, not IDs, connections, board regions or metadata.");
                Assert.Empty(ProfileLayoutRunner.Process(copy, false).Conflicts);
                Assert.Equal(0, ProfileLayoutRunner.Process(copy, true).Moved);
                Assert.Equal(saved, File.ReadAllText(copy));
            }
            finally { File.Delete(copy); }
        }, CancellationToken.None);
    }

    private static void MoveToOrigin(JsonObject graph) => Walk(graph, n => { n["x"] = 0; n["y"] = 0; });
    private static void RemoveCoordinates(JsonObject graph) => Walk(graph, n => { n.Remove("x"); n.Remove("y"); });
    private static void Walk(JsonObject graph, Action<JsonObject> action)
    {
        foreach (var node in graph["nodes"]!.AsArray().OfType<JsonObject>())
        {
            action(node);
            foreach (var key in new[] { "body", "bodyGraph" })
                if (node[key] is JsonObject body) Walk(body, action);
        }
    }
}
