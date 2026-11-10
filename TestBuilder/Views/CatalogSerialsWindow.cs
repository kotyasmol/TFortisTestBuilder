using System;
using System.Globalization;
using System.IO;
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
    public CatalogSerialsWindow(TestViewModel profile, EquipmentModel model)
    {
        Title = "Серия изделия";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var current = model.SerialSeries ?? new ModelSerialSeries();
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = model.Name, FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Сервер выдаёт номер. Стартовый номер задаёт нижнюю границу проверки.",
            TextWrapping = TextWrapping.Wrap });
        var serverType = AddField("Тип на сервере", current.ServerDeviceType);
        var first = AddField("Стартовый серийный номер", NumberText(current.FirstSerialNumber));
        var last = AddField("Последний серийный номер", NumberText(current.LastSerialNumber));
        var offset = AddField("База короткого номера", model.SerialSeries == null ? "" : NumberText(current.SerialOffset));
        var label = AddField("Код модели на этикетке", model.LabelCode?.ToString("D3", CultureInfo.InvariantCulture) ?? "");
        var mac = AddField("Префикс MAC", current.MacPrefix ?? "", "Четыре байта через двоеточие");
        var board = AddField("Имя платы при записи Pro MAC", current.BoardVersion ?? "");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "Сохранить в справочник", IsDefault = true };
        save.Click += (_, _) =>
        {
            try
            {
                profile.SaveCatalogModel(model with
                {
                    LabelCode = string.IsNullOrWhiteSpace(label.Text) ? null : ReadNumber(label, "код модели на этикетке"),
                    SerialSeries = current with
                    {
                        ServerDeviceType = serverType.Text?.Trim() ?? "",
                        FirstSerialNumber = ReadNumber(first, "стартовый номер"),
                        LastSerialNumber = ReadNumber(last, "последний номер"),
                        SerialOffset = ReadNumber(offset, "базу короткого номера"),
                        MacPrefix = Optional(mac),
                        BoardVersion = Optional(board)
                    }
                });
                Close();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
                InvalidOperationException or FormatException or System.Text.Json.JsonException)
            {
                status.Text = ex.Message;
            }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Content = panel;

        TextBox AddField(string caption, string value, string? hint = null)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("210,*") };
            row.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) });
            var field = new TextBox { Text = value, Watermark = hint };
            AutomationProperties.SetName(field, caption);
            Grid.SetColumn(field, 1);
            row.Children.Add(field);
            panel.Children.Add(row);
            return field;
        }
    }

    private static string NumberText(int number) => number.ToString(CultureInfo.InvariantCulture);
    private static string? Optional(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
    private static int ReadNumber(TextBox input, string caption) =>
        int.TryParse(input.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number : throw new FormatException($"Укажите {caption} целым числом.");
}
