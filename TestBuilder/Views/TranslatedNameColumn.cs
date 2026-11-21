using Avalonia.Controls;
using Avalonia.Data;
using TestBuilder.Converters;

namespace TestBuilder.Views;

// Show a translated human-readable name, but edit the original string using the
// standard DataGridTextColumn editor, including its commit/cancel behavior.
public sealed class TranslatedNameColumn : DataGridTextColumn
{
    public TranslatedNameColumn() => Binding = new Binding("Name") { Mode = BindingMode.TwoWay };

    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
        var control = base.GenerateElement(cell, dataItem);
        // Recycled cells receive a new DataContext; never pin the label to the previous row.
        control.Bind(TextBlock.TextProperty, new Binding("Name") {
            Mode = BindingMode.OneWay, Converter = new RussianUiTextConverter()
        });
        return control;
    }
}
