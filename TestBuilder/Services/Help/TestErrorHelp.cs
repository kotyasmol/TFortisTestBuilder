using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Platform;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Services.Help;

internal static class TestErrorHelp
{
    private sealed record Entry(TestErrorCode Code, string Title, string Group, string Summary, string[] Causes, string[] Checks);

    private static readonly Lazy<GuideArticle[]> Articles = new(Load);
    public static IReadOnlyList<GuideArticle> All => Articles.Value;

    private static GuideArticle[] Load()
    {
        using var stream = AssetLoader.Open(new Uri("avares://TestBuilder/Assets/Help/test-errors.json"));
        var entries = JsonSerializer.Deserialize<Entry[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Не удалось прочитать каталог ошибок.");
        return entries.OrderBy(entry => entry.Code).Select(entry =>
        {
            var code = entry.Code.Format();
            return new GuideArticle(code, $"{code} · {entry.Title}", entry.Group, entry.Summary,
            [
                new GuideBlock("Возможные причины", "text", entry.Causes),
                new GuideBlock("Что проверить", "steps", entry.Checks)
            ]);
        }).ToArray();
    }
}
