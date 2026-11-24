using System;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;

namespace TestBuilder.Views;

public sealed partial class HardwareEditorView
{
    // Explicitly save an independent copy of a profile device to the shared library.
    private async Task EditTemplateAsync(string? id, DeviceDefinition source)
    {
        if (_cancelInline != null) return;
        try
        {
            var result = await EditInlineAsync(new EquipmentTemplateEditor(_library, source, id), "Сохранить устройство в общий каталог");
            if (result != null) _status.Text = "Копия устройства сохранена в общем каталоге.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
}
