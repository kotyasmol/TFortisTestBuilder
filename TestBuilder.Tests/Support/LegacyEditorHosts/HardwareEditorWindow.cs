using Avalonia.Controls;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

// Legacy test fixture; the application opens HardwareEditorView in its workspace.
public sealed class HardwareEditorWindow : Window
{
    public HardwareEditorWindow(TestViewModel vm) : this(vm, new EquipmentLibrary()) { }
    internal HardwareEditorWindow(TestViewModel vm, EquipmentLibrary library)
    {
        Title = "Оборудование текущего профиля"; Width = 1180; Height = 780; MinWidth = 900; MinHeight = 600;
        var editor = new HardwareEditorView(vm, library); editor.Finished += Close; Content = editor;
    }
}
