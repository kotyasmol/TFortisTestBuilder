using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TestBuilder.Services.Graph;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services;

internal static class ProfileDependencies
{
    public static string Describe(GraphWorkspaceViewModel graph, EquipmentCatalog catalog)
    {
        var card = ProfileCatalogLink.Resolve(catalog, graph.CatalogModel, graph.DeviceModelId, graph.DeviceModel);
        var text = new List<string> { "Карточка изделия" };
        text.Add(card == null ? graph.UseCatalogSerials
            ? "Карточка не найдена. Добавьте изделие в справочник или импортируйте JSON с карточкой. Запуск с серийными номерами из справочника недоступен."
            : "Не задана. Профиль использует параметры нод." : ProfileCatalogLink.Describe(card));
        if (card != null)
        {
            text.Add(catalog.Sources.GetValueOrDefault(card.Id, "Из JSON профиля"));
            text.Add("При сохранении, копировании и экспорте карточка входит в JSON. Местный справочник имеет приоритет над резервной карточкой.");
            if (graph.CatalogModel is { } backup && !ProfileCatalogLink.Equivalent(backup, card))
                text.Add("Резервная карточка отличается. В файле сохранено:\n" + ProfileCatalogLink.Describe(backup));
        }
        var nodes = ProfileModelAdaptation.All(graph).ToArray();
        text.Add("\nВнешние файлы — передаются отдельно");
        var files = new List<string>();
        foreach (var node in nodes)
        {
            var value = node switch
            {
                SetProMacNodeViewModel p => FileStatus("Скрипт записи MAC", p.BatchPath, true),
                UpdatePswFirmwareNodeViewModel f => FileStatus("Прошивка", f.FirmwarePath, false),
                ClearArpCacheNodeViewModel { RunArpdBat: true } a => FileStatus("Скрипт очистки ARP", a.ArpdBatPath, true),
                RunProgramNodeViewModel p => FileStatus("Внешняя программа", p.ProgramPath, false) +
                    (string.IsNullOrWhiteSpace(p.WorkingDirectory) ? "" : "\nРабочая папка: " + p.WorkingDirectory),
                _ => null
            };
            if (value != null) files.Add(node.DisplayTitle + (node is SubtestNodeViewModel s ? " / " + s.Name : "") + "\n" + value);
        }
        text.Add(files.Count == 0 ? "Явные файловые зависимости в нодах не заданы." : string.Join("\n\n", files.Distinct()));
        if (files.Count > 0) text.Add("Файлы, вызываемые внутри скриптов и программ, проверьте отдельно. Пути в JSON при переносе не меняются.");
        var prints = nodes.OfType<PrintLabelNodeViewModel>().ToArray();
        if (prints.Length > 0)
        {
            text.Add("\nПечать");
            foreach (var print in prints)
                text.Add($"{print.CurrentTemplateName}: {(print.LabelTemplate == null ? "макет из общих настроек; экспорт вложит его копию в ноду" : "макет уже внутри JSON")}.\n" +
                    (print.UseDefaultPrinter ? "Принтер по умолчанию выбирается на каждом компьютере." : "Имя принтера в ноде: " + print.PrinterName) +
                    (print.UseLastTestLabel ? "\nПовтор последней этикетки использует историю тестов этого компьютера; история не входит в профиль." : ""));
            text.Add("Драйвер принтера, имя принтера и общие настройки компьютера не переносятся. Экспорт не меняет общие макеты получателя.");
        }
        text.Add("Общие настройки сервера, сессия лаунчера, идентификатор стенда, COM-порты и сетевые адаптеры проверяются на каждом компьютере отдельно.");
        return string.Join("\n\n", text);
    }

    private static string FileStatus(string label, string path, bool besideApp)
    {
        if (string.IsNullOrWhiteSpace(path)) return label + ": путь не задан.";
        if (!OperatingSystem.IsWindows() && (path.Contains('\\') || path.Length > 1 && path[1] == ':'))
            return label + ": " + path + "\nПуть Windows: проверьте наличие на стенде.";
        var found = File.Exists(path) || besideApp && File.Exists(Path.Combine(AppContext.BaseDirectory, path));
        return label + ": " + path + "\n" + (found ? "Файл найден на этом компьютере." : "Файл по указанному пути не найден. Для системной команды проверьте PATH на стенде.");
    }
}
