using Avalonia;
using Avalonia.Controls;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;

namespace TestBuilder.Views;

// Legacy standalone fixture used only by editor compatibility tests.
public sealed class EquipmentTemplateWindow : Window
{
    public EquipmentTemplateWindow(EquipmentLibrary library, DeviceDefinition source, string? id = null)
    {
        Title = id == null ? "Новое оборудование в каталоге" : "Изменить оборудование в каталоге";
        Width = 980; Height = 720; MinWidth = 800; MinHeight = 550;
        var editor = new EquipmentTemplateEditor(library, source, id) { Margin = new Thickness(20) };
        editor.Completed += result => Close(result); Content = editor;
    }
}
