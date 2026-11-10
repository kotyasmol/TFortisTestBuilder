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
        Width = 660;
        Height = 550;
        MinWidth = 510;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var current = model.SerialSeries ?? new ModelSerialSeries { ServerDeviceType = model.ServerDeviceType ?? "" };
        var general = new StackPanel { Spacing = 8 };
        var name = AddField(general, "Название изделия", model.Name);
        var aliases = AddField(general, "Альтернативные названия", string.Join(Environment.NewLine, model.Aliases ?? Array.Empty<string>()),
            "По одному в строке");
        aliases.AcceptsReturn = true;
        aliases.MinHeight = 60;
        var label = AddField(general, "Код модели на этикетке", model.LabelCode?.ToString("D3", CultureInfo.InvariantCulture) ?? "");
        general.Children.Add(new TextBlock { Text = "Комплектации этикетки", FontWeight = FontWeight.SemiBold });
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

        var serial = new StackPanel { Spacing = 8 };
        serial.Children.Add(Hint("Сервер выдаёт номер. Стартовый номер задаёт нижнюю границу проверки."));
        var serverType = AddField(serial, "Тип на сервере", current.ServerDeviceType, "Выдача серийных номеров");
        var first = AddField(serial, "Стартовый серийный номер", model.SerialSeries == null ? "" : NumberText(current.FirstSerialNumber));
        var last = AddField(serial, "Последний серийный номер", model.SerialSeries == null ? "" : NumberText(current.LastSerialNumber));
        var offset = AddField(serial, "База короткого номера", model.SerialSeries == null ? "" : NumberText(current.SerialOffset));
        var mac = AddField(serial, "Префикс MAC", current.MacPrefix ?? "", "Четыре байта через двоеточие");
        var board = AddField(serial, "Имя платы при записи Pro MAC", current.BoardVersion ?? "");

        var report = new StackPanel { Spacing = 8 };
        var useRtl = new CheckBox { Content = "Использовать отчёт RTL", IsChecked = model.RtlReport != null };
        report.Children.Add(useRtl);
        var rtlFields = new StackPanel { Spacing = 8, IsEnabled = useRtl.IsChecked == true };
        useRtl.IsCheckedChanged += (_, _) => rtlFields.IsEnabled = useRtl.IsChecked == true;
        report.Children.Add(rtlFields);
        var rtlCode = AddField(rtlFields, "Числовой тип отчёта на сервере", model.RtlReport == null ? "" : NumberText(model.RtlReport.DeviceType));
        var identifier = AddField(rtlFields, "Переменная идентификатора платы", model.RtlReport?.DeviceIdentifierVariable ?? "CpuId");
        var failure = AddField(rtlFields, "Служебный идентификатор отказов", model.RtlReport?.FailureDeviceIdentifier ?? "");
        var assignOnReport = new CheckBox
        {
            Content = "Получать серийный номер из ответа на отчёт",
            IsChecked = model.RtlReport?.AssignSerialOnReport == true
        };
        rtlFields.Children.Add(assignOnReport);
        rtlFields.Children.Add(Hint("При снятой галочке номер запрашивается отдельной нодой до отправки отчёта."));

        var tabs = new TabControl();
        AddTab("Изделие и этикетка", general);
        AddTab("Серийные номера", serial);
        AddTab("Отчёт RTL", report);
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
                    !string.IsNullOrWhiteSpace(offset.Text) || Optional(mac) != null || Optional(board) != null;
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
                    ServerDeviceType = hasRange ? null : server,
                    IsFullOverride = true,
                    SerialSeries = hasRange ? current with
                    {
                        ServerDeviceType = server!,
                        FirstSerialNumber = ReadNumber(first, "стартовый номер"),
                        LastSerialNumber = ReadNumber(last, "последний номер"),
                        SerialOffset = ReadNumber(offset, "базу короткого номера"),
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
        string columns = "210,*", string? accessibleName = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(columns) };
        row.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) });
        var field = new TextBox { Text = value, Watermark = hint };
        AutomationProperties.SetName(field, accessibleName ?? caption);
        Grid.SetColumn(field, 1);
        row.Children.Add(field);
        panel.Children.Add(row);
        return field;
    }

    private static string NumberText(int number) => number.ToString(CultureInfo.InvariantCulture);
    private static string? Optional(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
    private static int ReadNumber(TextBox input, string caption) =>
        int.TryParse(input.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number : throw new FormatException($"Укажите {caption} целым числом.");
}
