using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class PrinterPicker : UserControl
{
    public static readonly StyledProperty<string> PrinterNameProperty = AvaloniaProperty.Register<PrinterPicker, string>(
        nameof(PrinterName), "", defaultBindingMode: BindingMode.TwoWay);
    public string PrinterName { get => GetValue(PrinterNameProperty); set => SetValue(PrinterNameProperty, value); }
    public PrinterPickerViewModel State { get; }

    public PrinterPicker() : this(new WindowsPrinterCatalog()) { }
    internal PrinterPicker(IPrinterCatalog catalog)
    {
        State = new PrinterPickerViewModel(catalog);
        InitializeComponent();
        Loaded += async (_, _) => await State.EnsureLoadedAsync();
        Unloaded += (_, _) => State.CancelRefresh();
    }
    private void OnChoosePrinter(object? sender, RoutedEventArgs e)
    {
        PrinterNameInput.Focus();
        PrinterNameInput.IsDropDownOpen = !PrinterNameInput.IsDropDownOpen;
    }
}
