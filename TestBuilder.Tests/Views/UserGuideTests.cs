using System.Net;
using System.Text.RegularExpressions;
using Avalonia.Headless;
using TestBuilder.Services.Help;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class UserGuideTests
{
    [Fact]
    public async Task ArticlesHaveStableRoutesAndCompleteBlocks()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            Assert.Equal(UserGuide.All.Count, UserGuide.All.Select(a => a.Id).Distinct().Count());
            foreach (var id in new[] { "guide-overview", "profile-new", "profile-copy", "profile-transfer", "catalog-rules", "storage",
                "hardware-maps", "node-controls", "network-fields", "datatest-algorithm", "label-layout", "label-transfer", "update-and-release", "recipes" })
                Assert.Single(UserGuide.All, a => a.Id == id);
            foreach (var article in UserGuide.All)
            {
                Assert.Matches("^[a-zA-Z0-9-]+$", article.Id);
                Assert.False(string.IsNullOrWhiteSpace(article.Title));
                Assert.False(string.IsNullOrWhiteSpace(article.Summary));
                Assert.False(string.IsNullOrWhiteSpace(article.Group));
                Assert.NotEmpty(article.Blocks);
                foreach (var block in article.Blocks)
                {
                    Assert.False(string.IsNullOrWhiteSpace(block.Heading));
                    Assert.Contains(block.Kind, new[] { "text", "list", "steps", "fields" });
                    Assert.NotEmpty(block.Items);
                    Assert.All(block.Items, s => Assert.False(string.IsNullOrWhiteSpace(s)));
                }
                if (article.Id.EndsWith("NodeViewModel", StringComparison.Ordinal))
                    Assert.NotEmpty(Assert.Single(article.Blocks, b => b.Kind == "steps").Items);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SearchIsRestrictedToTheChosenSectionAndExportRetainsEveryArticle()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var all = UserGuide.Search("серий");
            Assert.Contains(all, a => a.Group != "Справочник нод");
            var nodes = UserGuide.Search("серий", "Справочник нод");
            Assert.NotEmpty(nodes);
            Assert.All(nodes, a => Assert.Equal("Справочник нод", a.Group));
            Assert.All(nodes, a => Assert.Contains(a, all));
            Assert.Empty(UserGuide.Search("серий", "несуществующий раздел"));
            Assert.Equal(UserGuide.All.Count, UserGuide.Search("   ").Length);

            var html = UserGuide.ExportHtml();
            var ids = Regex.Matches(html, "id=\"([^\"]+)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
            var links = Regex.Matches(html, "href=\"#([^\"]+)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToArray();
            Assert.Equal(UserGuide.All.Count + UserGuide.Groups.Length, links.Length);
            Assert.All(links, id => Assert.Contains(id, ids));
            Assert.All(UserGuide.All, a => Assert.Contains(a.Id, ids));
            Assert.Equal(UserGuide.Groups.Length, Regex.Matches(html, "<section ").Count);
            Assert.Equal(UserGuide.All.Count, Regex.Matches(html, "<article ").Count);
            var text = UserGuide.PlainText();
            Assert.All(UserGuide.Groups, group => Assert.Contains(group, text));
            Assert.All(UserGuide.All, a => Assert.Contains(a.BodyText, text));
        }, CancellationToken.None);
    }
}
