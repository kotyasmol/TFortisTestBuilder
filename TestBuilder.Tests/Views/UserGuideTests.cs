using System.Net;
using System.Text.RegularExpressions;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Views;
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
                "hardware-maps", "data-scopes", "node-controls", "network-fields", "datatest-algorithm", "label-types", "label-data", "label-layout", "label-transfer", "update-and-release", "catalog-to-prod", "recipes" })
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
            foreach (var term in new[] { "Типы этикеток", "Что печатать в строке SN", "Что кодировать в штрихкоде",
                         "Цифр SN в тексте", "Формат данных штрихкода", "03200428", "00600428", "00428", "catalog/label-types.json",
                         "Сохранить тип", "полный SN", "сканером", "последнего теста", "не обрезается молча" })
                Assert.Contains(term, article.BodyText);
            var node = Assert.Single(UserGuide.All, a => a.Id == "PrintLabelNodeViewModel");
            Assert.Contains("03200428", node.BodyText);
            Assert.DoesNotContain("целиком печатается", node.BodyText);
            Assert.Contains("id=\"label-data\"", UserGuide.ExportHtml());
            Assert.Contains("SerialTail", UserGuide.PlainText());
        }, default);
    }

    [Fact]
    public async Task SharedLabelTypesHelpDescribesTheMainInterfacePriorityAndDraftTransfer()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var article = Assert.Single(UserGuide.Search("Отступ всего макета", "Этикетки"), a => a.Id == "label-types");
            foreach (var term in new[] { "Этикетки → Типы этикеток", "Высота штрихкода, мм", "Ширина полосы, точек",
                "SN и штрихкод", "Пробная печать: 1 шт.", "Сохранить тип", "catalog/label-types.json", "reference/labels/types.json",
                "Импорт типов…", "черновики", "приоритет", "Повтор", "Новая печать", "старые программы", "не публикует",
                "три универсальных типа", "название устройства", "00:00:00:00:00:00", "00000000", "Шкаф 1",
                "Макет по умолчанию", "25,4 × 13,4 мм", "4,23 мм" })
                Assert.Contains(term, article.BodyText);
            foreach (var term in new[] { "Новый тип", "Создать копию", "Название типа", "Источники данных", "Имя переменной",
                "Привязки изделий", "Сохранить привязки", "Автоматически по изделию", "schemaVersion=2", "Dut.default_mac",
                "Постоянное значение", "успешный шаг до печати", "Ручная печать", "снимок" })
                Assert.Contains(term, article.BodyText);
            foreach (var term in new[] { "Что печатать в элементе", "Свой текст / формат", "столбца", "Образец остаётся видимым" })
                Assert.Contains(term, article.BodyText);
            var nodeGuide = Assert.Single(UserGuide.All, a => a.Id == "PrintLabelNodeViewModel");
            foreach (var term in new[] { "Этикетка ноды…", "в области графа", "Применить к ноде", "Отмена", "Общие типы этикеток" })
                Assert.Contains(term, nodeGuide.BodyText);
            Assert.DoesNotContain("Выбрать / настроить шаблон…", nodeGuide.BodyText);
            Assert.Contains(UserGuide.Search("Привязки изделий", "Этикетки"), a => a.Id == article.Id);
            Assert.DoesNotContain(UserGuide.Search("Привязки изделий", "Сеть и DataTest"), a => a.Id == article.Id);
            var html = WebUtility.HtmlDecode(UserGuide.ExportHtml());
            Assert.Contains("id=\"label-types\"", html); Assert.Contains(article.Title, html);
            Assert.DoesNotContain(UserGuide.Search("Отступ всего макета", "Справочник нод"), a => a.Id == article.Id);
            Assert.DoesNotContain("выберите обновлённый макет в ноде заново", UserGuide.PlainText());
            var window = new UserGuideView();
            try
            {
                window.Show(); Dispatcher.UIThread.RunJobs();
                var section = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "GuideSection");
                var search = window.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "GuideSearch");
                var articles = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "GuideArticles");
                section.SelectedItem = "Этикетки"; search.Text = "Имя переменной"; Dispatcher.UIThread.RunJobs();
                Assert.Equal(article.Id, Assert.IsType<GuideArticle>(articles.SelectedItem).Id);
                section.SelectedItem = "Сеть и DataTest"; Dispatcher.UIThread.RunJobs();
                Assert.Empty(articles.ItemsSource!.Cast<GuideArticle>()); Assert.Null(articles.SelectedItem);
                section.SelectedItem = "Этикетки"; Dispatcher.UIThread.RunJobs();
                Assert.Equal(article.Id, Assert.IsType<GuideArticle>(articles.SelectedItem).Id);
            }
            finally { window.Close(); }
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
