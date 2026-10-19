using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Platform;

namespace TestBuilder.Services.Help;

public sealed record GuideBlock(string Heading, string Kind, string[] Items);
public sealed record GuideArticle(string Id, string Title, string Group, string Summary, GuideBlock[] Blocks)
{
    public string BodyText => Summary + "\n\n" + string.Join("\n\n", Blocks.Select(b => b.Heading + "\n" + string.Join("\n", b.Items.Select((s, i) => b.Kind == "steps" ? $"{i + 1}. {s}" : s))));
    public override string ToString() => Title;
}

public static class UserGuide
{
    private static readonly Lazy<GuideArticle[]> Articles = new(Load);
    public static IReadOnlyList<GuideArticle> All => Articles.Value;
    private static GuideArticle[] Load()
    {
        using var stream = AssetLoader.Open(new Uri("avares://TestBuilder/Assets/Help/user-guide.json"));
        return JsonSerializer.Deserialize<GuideArticle[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Array.Empty<GuideArticle>();
    }
    public static GuideArticle? ForNode(Type type) => All.FirstOrDefault(a => a.Id == type.Name);
    public static GuideArticle[] Search(string query) => All.Where(a => (a.Title + "\n" + a.BodyText).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    public static string PlainText() => "Инструкция TFortis TestBuilder\n\n" + string.Join("\n\n", All.Select(a => a.Title + "\n\n" + a.BodyText));
    public static string ExportHtml()
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var html = new StringBuilder("<!doctype html><html lang=\"ru\"><meta charset=\"utf-8\"><title>Инструкция TFortis TestBuilder</title><style>body{font:17px/1.6 system-ui,sans-serif;color:#243041;background:#f1f3f5;max-width:900px;margin:40px auto;padding:0 28px}h1{font-size:30px}h2{font-size:25px}h3{font-size:19px}article{margin:48px 0;border-top:1px solid #9aa9bb;padding-top:20px}a{color:#2457a6}li{padding-bottom:9px}p{margin:12px 0}@media print{body{background:white;margin:0;font-size:12pt}nav{break-after:page}article{break-before:page}h2,h3{break-after:avoid}li{break-inside:avoid}}</style><h1>Инструкция TFortis TestBuilder</h1><nav><h2>Содержание</h2><ul>");
        foreach (var a in All) html.Append($"<li><a href=\"#{E(a.Id)}\">{E(a.Title)}</a></li>");
        html.Append("</ul></nav>");
        foreach (var a in All)
        {
            html.Append($"<article id=\"{E(a.Id)}\"><h2>{E(a.Title)}</h2><p>{E(a.Summary)}</p>");
            foreach (var b in a.Blocks)
            {
                html.Append($"<h3>{E(b.Heading)}</h3>");
                var tag = b.Kind == "steps" ? "ol" : "ul";
                html.Append($"<{tag}>");
                foreach (var item in b.Items) html.Append($"<li>{E(item)}</li>");
                html.Append($"</{tag}>");
            }
            html.Append("</article>");
        }
        return html.Append("</html>").ToString();
    }
}
