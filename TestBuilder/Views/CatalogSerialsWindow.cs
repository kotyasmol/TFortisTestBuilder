using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

internal sealed class CatalogSerialsWindow : Window
{
    public CatalogSerialsWindow(TestViewModel profile, EquipmentModel model, bool createNew = false, bool selectForProfile = true)
    {
        Title = createNew ? "Добавить изделие в справочник" : "Редактировать изделие";
        Width = 760;
        Height = 720;
        MinWidth = 510;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var current = model.SerialSeries ?? new ModelSerialSeries { ServerDeviceType = model.ServerDeviceType ?? "" };
        var suppliedModel = EquipmentCatalog.LoadBundled().Resolve(model.Id, null);
        var exampleModel = model.SerialSeries != null ? model : suppliedModel?.SerialSeries != null ? suppliedModel :
            profile.EquipmentModels.FirstOrDefault(item => item.SerialSeries != null);
        var example = exampleModel?.SerialSeries;
        var proExample = model.SerialSeries?.BoardVersion != null ? model :
            profile.EquipmentModels.FirstOrDefault(item => item.SerialSeries?.BoardVersion != null);
        var rtlExample = model.RtlReport != null ? model : profile.EquipmentModels.FirstOrDefault(item => item.RtlReport != null);
        var general = new StackPanel { Spacing = 8 };
        var name = AddField(general, "Название изделия", model.Name,
            help: "Обязательно. Название в списке изделий, профилях и тексте этикетки.");
        var aliases = AddField(general, "Альтернативные названия", string.Join(Environment.NewLine, model.Aliases ?? Array.Empty<string>()),
            "По одному в строке", help: "Необязательно. Прежние названия этой же модели для поиска и старых профилей. Каждое — с новой строки.");
        aliases.AcceptsReturn = true;
        aliases.MinHeight = 60;
        var label = AddField(general, "Код модели на этикетке", model.LabelCode?.ToString("D3", CultureInfo.InvariantCulture) ?? "",
            help: "Для этикеток. Три цифры из утверждённого справочника этикеток (000–999). Это код модели в штрихкоде, а не начало серийного номера.");
        general.Children.Add(new TextBlock { Text = "Комплектации этикетки", FontWeight = FontWeight.SemiBold });
        general.Children.Add(Hint("В строках 0–9 укажите названия нужных вариантов, например «Plastim + PWR180». Код строки станет первой цифрой штрихкода. Неиспользуемые строки оставьте пустыми."));
        var variants = new TextBox[10];
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        for (var column = 0; column < 2; column++)
        {
            var fields = new StackPanel { Spacing = 6 };
            for (var code = column * 5; code < column * 5 + 5; code++)
                variants[code] = AddField(fields, NumberText(code), model.LabelVariants?.GetValueOrDefault(code) ?? "",
                    "Название комплектации", "38,*", $"Название комплектации {code}");
            Grid.SetColumn(fields, column * 2);
            columns.Children.Add(fields);
        }
        general.Children.Add(columns);

        var cabinetsPanel = new StackPanel { Spacing = 8 };
        cabinetsPanel.Children.Add(Hint("Для каждого варианта укажите короткое название шкафа, которое нужно напечатать, например «Plastim». Название комплектации с этим кодом должно быть заполнено на первой вкладке. Пустая подпись — печать без шкафа."));
        var cabinets = new TextBox[10];
        for (var code = 0; code < cabinets.Length; code++)
            cabinets[code] = AddField(cabinetsPanel, $"Комплектация {code}", model.LabelCabinets?.GetValueOrDefault(code) ?? "",
                "Например, Fibox или Plastim", accessibleName: $"Шкаф комплектации {code}");

        var serial = new StackPanel { Spacing = 8 };
        serial.Children.Add(Hint("Здесь задаются правила для всей модели. Конкретный номер устройства выдаёт сервер. Параметры новой серии получите у администратора сервера или инженера. Для штатной ZPL-этикетки коммутатора диапазон и база обязательны."));
        var serverType = AddField(serial, "Тип на сервере", current.ServerDeviceType,
            help: "Для выдачи номеров. Точное имя зарегистрированного серверного типа, включая +, дефисы и пробелы. Оно может отличаться от названия изделия. Карточка не создаёт тип на сервере.");
        var first = AddField(serial, "Стартовый серийный номер", model.SerialSeries == null ? "" : NumberText(current.FirstSerialNumber),
            help: "Первый допустимый полный номер серии. Не номер текущего устройства." + Example(example?.FirstSerialNumber, exampleModel?.Name));
        var last = AddField(serial, "Последний серийный номер", model.SerialSeries == null ? "" : NumberText(current.LastSerialNumber),
            help: "Последний допустимый полный номер серии. Он не должен быть меньше стартового." + Example(example?.LastSerialNumber, exampleModel?.Name));
        var offset = AddField(serial, "База короткого номера", model.SerialSeries == null ? "" : NumberText(current.SerialOffset),
            help: "Число, которое вычитается из полного номера для MAC и штрихкода. Берите из параметров серии." +
                (example == null ? "" : $" Пример для {exampleModel!.Name}: {example.FirstSerialNumber} − {example.SerialOffset} = {example.FirstSerialNumber - example.SerialOffset:D5}."));
        var failureSerial = AddField(serial, "Номер для аварийного отчёта",
            current.FailureReportSerialNumber is { } fallback ? NumberText(fallback) : "",
            help: "Необязательно. Утверждённый служебный номер для отказа до получения номера устройства. Должен входить в серию. Согласуйте с администратором; не подставляйте номер случайного изделия.");
        var mac = AddField(serial, "Префикс MAC", current.MacPrefix ?? "", "Четыре байта через двоеточие",
            help: "Если профиль рассчитывает MAC: первые четыре байта из документации изделия. Два последних рассчитываются из короткого номера (0–65535). Без расчёта MAC оставьте пустым." +
                (example?.MacPrefix is { } prefix ? $" Пример для {exampleModel!.Name}: {prefix}." : ""));
        var board = AddField(serial, "Имя платы при записи Pro MAC", current.BoardVersion ?? "",
            help: "Только для MAC серии Pro. Название платы, которое ожидает программа записи MAC; согласуйте с инженером. Для другого способа записи оставьте пустым." +
                (proExample == null ? "" : $" Пример для {proExample.Name}: {proExample.SerialSeries!.BoardVersion}."));
        var supplied = suppliedModel?.SerialSeries;
        if (supplied != null)
        {
            var fill = new Button { Content = "Заполнить из поставки" };
            fill.Click += (_, _) =>
            {
                serverType.Text = supplied.ServerDeviceType;
                first.Text = NumberText(supplied.FirstSerialNumber);
                last.Text = NumberText(supplied.LastSerialNumber);
                offset.Text = NumberText(supplied.SerialOffset);
                failureSerial.Text = supplied.FailureReportSerialNumber is { } number ? NumberText(number) : "";
                mac.Text = supplied.MacPrefix ?? "";
                board.Text = supplied.BoardVersion ?? "";
            };
            serial.Children.Add(fill);
        }
        serial.Children.Add(Hint("Для серии заполните вместе стартовый номер, последний номер и базу. Без диапазона оставьте пустыми также префикс MAC, имя платы Pro и номер аварийного отчёта."));

        var report = new StackPanel { Spacing = 8 };
        var useRtl = new CheckBox { Content = "Использовать отчёт RTL", IsChecked = model.RtlReport != null };
        report.Children.Add(useRtl);
        report.Children.Add(Hint("Для плат с серверным форматом отчёта RTL. Для обычного отчёта коммутатора этот флажок не нужен."));
        var rtlFields = new StackPanel { Spacing = 8, IsEnabled = useRtl.IsChecked == true };
        useRtl.IsCheckedChanged += (_, _) => rtlFields.IsEnabled = useRtl.IsChecked == true;
        report.Children.Add(rtlFields);
        var rtlCode = AddField(rtlFields, "Числовой тип отчёта на сервере", model.RtlReport == null ? "" : NumberText(model.RtlReport.DeviceType),
            help: "Положительный числовой код формата RTL из серверного протокола. Получите у администратора. Это не код модели на этикетке." +
                Example(rtlExample?.RtlReport?.DeviceType, rtlExample?.Name));
        var identifier = AddField(rtlFields, "Переменная идентификатора платы", model.RtlReport?.DeviceIdentifierVariable ?? "CpuId",
            help: "Имя переменной графа, куда нода чтения сохраняет уникальный ID платы, например CpuId или McuId. Здесь нужно имя переменной, а не сам ID и не серийный номер.");
        var failure = AddField(rtlFields, "Служебный идентификатор отказов", model.RtlReport?.FailureDeviceIdentifier ?? "",
            help: "Необязательно. Готовый служебный ID серверной записи для отказов до чтения ID платы. Получите у администратора; это строковый идентификатор, а не номер устройства.");
        var assignOnReport = new CheckBox
        {
            Content = "Получать серийный номер из ответа на отчёт",
            IsChecked = model.RtlReport?.AssignSerialOnReport == true
        };
        rtlFields.Children.Add(assignOnReport);
        rtlFields.Children.Add(Hint("Включите, если сервер выдаёт номер в ответе на RTL-отчёт (схема RTL-POE). Оставьте выключенным, если номер запрашивается отдельной нодой до отчёта (схема RTL-SW). Выбор определяется протоколом модели."));

        var tabs = new TabControl();
        AddTab("Изделие и этикетка", general);
        AddTab("Серийные номера", serial);
        AddTab("Отчёт RTL", report);
        AddTab("Шкаф на этикетке", cabinetsPanel);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "Сохранить в справочник", IsDefault = true };
        save.Click += (_, _) =>
        {
            try
            {
                var server = Optional(serverType);
                var hasRange = !string.IsNullOrWhiteSpace(first.Text) || !string.IsNullOrWhiteSpace(last.Text) ||
                    !string.IsNullOrWhiteSpace(offset.Text) || Optional(failureSerial) != null || Optional(mac) != null || Optional(board) != null;
                if (server == null && (hasRange || useRtl.IsChecked == true))
                    throw new FormatException("Укажите тип на сервере.");
                var newName = Optional(name) ?? "";
                IEnumerable<string> alternativeNames = (aliases.Text ?? "").Split(new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var nameKey = EquipmentCatalog.Normalize(newName);
                if (!createNew && EquipmentCatalog.Normalize(model.Name) != nameKey)
                    alternativeNames = alternativeNames.Append(model.Name);
                var labelVariants = variants.Select((field, code) => (Code: code, Name: Optional(field)))
                    .Where(item => item.Name != null).ToDictionary(item => item.Code, item => item.Name!);
                var updated = model with
                {
                    Name = newName,
                    Aliases = alternativeNames.Where(value => EquipmentCatalog.Normalize(value) is { Length: > 0 } key && key != nameKey)
                        .DistinctBy(EquipmentCatalog.Normalize).ToArray(),
                    LabelCode = string.IsNullOrWhiteSpace(label.Text) ? null : ReadNumber(label, "код модели на этикетке"),
                    LabelVariants = labelVariants.Count == 0 ? null : labelVariants,
                    LabelCabinets = cabinets.Select((field, code) => (Code: code, Name: Optional(field)))
                        .Where(item => item.Name != null).ToDictionary(item => item.Code, item => item.Name!),
                    ServerDeviceType = hasRange ? null : server,
                    IsFullOverride = true,
                    SerialSeries = hasRange ? current with
                    {
                        ServerDeviceType = server!,
                        FirstSerialNumber = ReadNumber(first, "стартовый номер"),
                        LastSerialNumber = ReadNumber(last, "последний номер"),
                        SerialOffset = ReadNumber(offset, "базу короткого номера"),
                        FailureReportSerialNumber = Optional(failureSerial) == null ? null : ReadNumber(failureSerial, "номер для аварийного отчёта"),
                        MacPrefix = Optional(mac),
                        BoardVersion = Optional(board)
                    } : null,
                    RtlReport = useRtl.IsChecked == true ? (model.RtlReport ?? new RtlReportSettings(0, false)) with
                    {
                        DeviceType = ReadNumber(rtlCode, "числовой тип отчёта"),
                        DeviceIdentifierVariable = Optional(identifier) ?? "",
                        FailureDeviceIdentifier = Optional(failure),
                        AssignSerialOnReport = assignOnReport.IsChecked == true
                    } : null
                };
                profile.SaveCatalogModel(updated, createNew, selectForProfile, createNew ? null : model);
                Close(updated);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or FormatException or System.Text.Json.JsonException)
            {
                status.Text = ex.Message;
            }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        var footer = new StackPanel { Spacing = 8, Children = { status, buttons } };
        var root = new DockPanel { Margin = new Thickness(20), LastChildFill = true };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(tabs);
        Content = root;

        void AddTab(string title, StackPanel fields)
        {
            fields.Margin = new Thickness(0, 12, 10, 12);
            tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = fields } });
        }
    }

    private static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static TextBox AddField(StackPanel panel, string caption, string value, string? hint = null,
        string columns = "210,*", string? accessibleName = null, string? help = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(columns) };
        row.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) });
        var field = new TextBox { Text = value, Watermark = hint };
        AutomationProperties.SetName(field, accessibleName ?? caption);
        Grid.SetColumn(field, 1);
        row.Children.Add(field);
        panel.Children.Add(row);
        if (help != null)
        {
            var description = Hint(help);
            description.FontSize = 12;
            description.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(description);
            ToolTip.SetTip(field, help);
            AutomationProperties.SetHelpText(field, help);
        }
        return field;
    }

    private static string Example(int? number, string? modelName) => number is { } value ? $" Пример для {modelName}: {value}." : "";

    private static string NumberText(int number) => number.ToString(CultureInfo.InvariantCulture);
    private static string? Optional(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
    private static int ReadNumber(TextBox input, string caption) =>
        int.TryParse(input.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number : throw new FormatException($"Укажите {caption} целым числом.");
}
