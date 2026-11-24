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
                "hardware-maps", "data-scopes", "node-controls", "network-fields", "datatest-algorithm", "label-data", "label-layout", "label-transfer", "update-and-release", "catalog-to-prod", "recipes" })
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
    public async Task LabelDataRulesAreSearchableAndExplainIdentityStorageAndScannerLimits()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var article = Assert.Single(UserGuide.Search("SerialTail", "Этикетки"), a => a.Id == "label-data");
            Assert.DoesNotContain(UserGuide.Search("SerialTail", "Справочник нод"), a => a.Id == article.Id);
            foreach (var term in new[] { "Формирование SN и штрихкода", "Что печатать в строке SN", "Что кодировать в штрихкоде",
                         "Цифр SN в тексте", "Формат данных штрихкода", "03200428", "00600428", "00428", "catalog/label-templates.json",
                         "Применить к ноде", "Сохранить каталог", "полный SN", "сканером", "последнего теста", "не обрезается молча" })
                Assert.Contains(term, article.BodyText);
            var node = Assert.Single(UserGuide.All, a => a.Id == "PrintLabelNodeViewModel");
            Assert.Contains("03200428", node.BodyText);
            Assert.DoesNotContain("целиком печатается", node.BodyText);
            Assert.Contains("id=\"label-data\"", UserGuide.ExportHtml());
            Assert.Contains("SerialTextFormat", UserGuide.PlainText());
        }, default);
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
            Assert.Contains(UserGuide.Search("IPv4 и DNS", "Сеть и DataTest"), a => a.Id == "network");
            Assert.DoesNotContain(UserGuide.Search("IPv4 и DNS", "Справочник нод"), a => a.Id == "network");
            Assert.Contains(UserGuide.Search("PROD", "Каталог и хранение данных"), a => a.Id == "catalog-to-prod");
            Assert.DoesNotContain(UserGuide.Search("PROD", "Справочник нод"), a => a.Id == "catalog-to-prod");
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

    [Fact]
    public async Task AddingPairsIsDocumentedWithItsFieldsSavingAndProfileHandoff()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var matches = UserGuide.Search("Добавить пары", "Сеть и DataTest");
            var article = Assert.Single(matches, a => a.Id == "network-ports");
            Assert.DoesNotContain(UserGuide.Search("Добавить пары", "Справочник нод"), a => a.Id == article.Id);
            foreach (var term in new[] { "Количество пар", "Мбит/с теста", "Названия и адреса", "512 пар", "testbuilder.settings",
                         "Взять пары из плана сети", "Сохранить план", "Отмена", "без отдельного окна" })
                Assert.Contains(term, article.BodyText);
            var exported = WebUtility.HtmlDecode(UserGuide.ExportHtml());
            Assert.Contains(article.Title, exported);
            Assert.Contains("Количество пар", exported);
            Assert.Contains("За одно нажатие можно добавить от 1 до 512 пар", exported);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SharedCatalogAndProfileBoundariesAreSearchableAndExported()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var article = Assert.Single(UserGuide.Search("Общие каталоги", "Каталог и хранение данных"), a => a.Id == "data-scopes");
            Assert.DoesNotContain(UserGuide.Search("Общие каталоги", "Справочник нод"), a => a.Id == article.Id);
            foreach (var term in new[] { "Оборудование профиля…", "Настройки компьютера", "Серия и этикетка", "Экспорт каталога…",
                         "Импортировать в каталог", "SchemaVersion=1", "Взять пары из плана сети", "Сохранить профиль" })
                Assert.Contains(term, article.BodyText);
            var html = WebUtility.HtmlDecode(UserGuide.ExportHtml());
            Assert.Contains(article.Title, html); Assert.Contains("Импортировать в каталог", html);
            var hardware = UserGuide.All.Single(a => a.Id == "hardware");
            Assert.DoesNotContain("Добавить выбранное в профиль", hardware.BodyText);
            Assert.DoesNotContain("Нажмите «Оборудование» в верхней панели", hardware.BodyText);
        }, CancellationToken.None);
    }
}
