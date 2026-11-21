using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System.Threading;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed class LabelLayoutEditorWindow : Window
{
    public LabelLayoutEditorViewModel Editor { get; }
    public LabelLayoutEditorWindow(LabelTemplateDefinition source)
    {
        Editor = new(source); DataContext = Editor;
        Title = "Редактор этикетки"; Width = 1280; Height = 880; MinWidth = 1040; MinHeight = 700;
        var root = new DockPanel { Margin = new Thickness(16) };
        var status = new TextBlock { Name = "LabelLayoutError", TextWrapping = TextWrapping.Wrap };
        var inputs = new List<Control>();
        Button Button(string title, Action action)
        {
            var button = new Button { Content = title, Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => { try { action(); status.Text = ""; } catch (Exception ex) { status.Text = ex.Message; } }; return button;
        }
        var footer = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) }; footer.Children.Add(status);
        var footerButtons = new WrapPanel();
        footerButtons.Children.Add(Button("Применить макет", () =>
        {
            Focus();
            if (HasInputErrors()) throw new InvalidOperationException("Исправьте значения в полях.");
            Close(Editor.Apply());
        }));
        footerButtons.Children.Add(Button("Отмена", () => Close(null))); footer.Children.Add(footerButtons);
        var testRow = new WrapPanel();
        var printer = new PrinterPicker { PrinterName = AppSettings.Instance.DefaultPrinterName ?? "", Width = 280 };
        testRow.Children.Add(printer);
        var print = new Button { Content = "Пробная печать: 1 шт.", Name = "PrintLayoutSample", Margin = new Thickness(8, 0, 0, 0) };
        CancellationTokenSource? printing = null;
        print.Click += async (_, _) =>
        {
            if (printing != null) return;
            TestViewModel? lockedTest = null;
            try
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.DataContext is MainWindowViewModel main)
                {
                    if (!main.LabelPrintingVM.CanEdit || main.TestVM.IsLabelToolBusy || main.TestVM.IsNetworkSetupBusy)
                        throw new InvalidOperationException("Пробная печать доступна в инженерном режиме после завершения текущей операции.");
                    lockedTest = main.TestVM; lockedTest.IsLabelToolBusy = true;
                }
                if (HasInputErrors()) throw new InvalidOperationException("Исправьте поля макета.");
                var template = Editor.Apply(); template.CabinetName = Editor.Cabinet;
                if (string.IsNullOrWhiteSpace(printer.PrinterName)) throw new InvalidOperationException("Выберите принтер.");
                printing = new(); print.IsEnabled = false; status.Text = "Пробная печать…";
                var step = new PrintLabelStep(LoggingService.Instance.CreateLogger("Пробная этикетка"), printer.PrinterName, "SerialNumber", "SerialShort", "Mac",
                    true, Editor.Serial, Editor.Mac, 1, template.UseShortSerial, true, template: template);
                var context = new TestContext(new RegisterState());
                var result = await step.ExecuteAsync(context, printing.Token);
                status.Text = result == StepResult.False ? context.GetVariable<string>("PrintLabel.Error") : "Этикетка передана принтеру. Проверьте считывание сканером.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { printing?.Dispose(); printing = null; print.IsEnabled = true; if (lockedTest != null) lockedTest.IsLabelToolBusy = false; }
        };
        Closed += (_, _) => printing?.Cancel(); testRow.Children.Add(print);
        var exportPng = new Button { Content = "Экспорт PNG…", Margin = new Thickness(8, 0, 0, 0) };
        exportPng.Click += async (_, _) =>
        {
            try
            {
                Editor.Apply();
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Предпросмотр этикетки", SuggestedFileName = "label-preview.png" });
                if (file == null) return;
                var previewControl = new LabelPreviewControl { Preview = Editor.Preview,
                    Width = Editor.Template.WidthDots + 4, Height = (Editor.Template.HeightDots ?? 150) + 4 };
                previewControl.Measure(Size.Infinity); previewControl.Arrange(new Rect(previewControl.DesiredSize));
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)previewControl.Width, (int)previewControl.Height));
                bitmap.Render(previewControl);
                await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); bitmap.Save(stream);
                status.Text = "Предпросмотр сохранён: один пиксель на точку принтера, рамка 2 пикселя. Шрифты приблизительные.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        testRow.Children.Add(exportPng); footer.Children.Add(testRow);
        footer.Children.Add(new TextBlock { Text = "Для пробы используются «Данные для примера и сканер». Операции с устройством и сервером не выполняются.", TextWrapping = TextWrapping.Wrap });
        footer.Children.Add(new TextBlock { Text = "«Применить макет» возвращает черновик. Чтобы перенести настройки, сохраните каталог в окне шаблонов.", TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = "Выберите элемент на этикетке или в списке. Перетаскивайте его мышью; угол меняет размер. Стрелки — сдвиг на точку, Shift — на 10 точек.", TextWrapping = TextWrapping.Wrap });
        var paper = new WrapPanel();
        Numeric(paper, "Ширина, мм", "PaperWidth", 1, 800); Numeric(paper, "Высота, мм", "PaperHeight", 1, 2000); Numeric(paper, "Смещение X принтера, мм", "Origin", 0, 1800);
        Combo(paper, "Разрешение, dpi", "Dpi", new object[] { 203, 300 }); Combo(paper, "Язык", "Language", new object[] { "ZPL", "EPL" });
        Numeric(paper, "Яркость ZPL", "Darkness", -30, 30); top.Children.Add(paper); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("205,12,*,12,270") };
        var left = new DockPanel(); var actions = new WrapPanel();
        foreach (var kind in LabelLayoutEditorViewModel.Kinds) actions.Children.Add(Button(kind.Key == "Text" ? "Текст" : kind.Key == "Barcode" ? "Штрихкод" : "Рамка", () => Editor.Add(kind.Key)));
        actions.Children.Add(Button("Копировать", Editor.CopySelected)); actions.Children.Add(Button("Удалить", Editor.Remove));
        actions.Children.Add(Button("По центру", Editor.Center));
        var up = Button("↑", () => Reorder(-1)); ToolTip.SetTip(up, "Выше в списке и раньше при печати"); actions.Children.Add(up);
        var down = Button("↓", () => Reorder(1)); ToolTip.SetTip(down, "Ниже в списке и позже при печати"); actions.Children.Add(down);
        DockPanel.SetDock(actions, Dock.Bottom); left.Children.Add(actions);
        var list = new ListBox { Name = "LabelElements", ItemsSource = Editor.Elements,
            ItemTemplate = new FuncDataTemplate<LabelLayoutElement>((_, _) => { var text = new TextBlock { TextWrapping = TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding("Name")); return text; }) };
        list.Bind(ListBox.SelectedItemProperty, new Binding("Selected") { Mode = BindingMode.TwoWay }); left.Children.Add(list); main.Children.Add(left);
        var center = new Grid { RowDefinitions = new RowDefinitions("3*,2*") };
        var canvas = new LabelLayoutCanvas(Editor) { MinHeight = 140, Margin = new Thickness(4) }; center.Children.Add(canvas);
        var samples = new WrapPanel();
        Text(samples, "Серийный номер для примера", "Serial", "LayoutSerial"); Text(samples, "MAC для примера", "Mac", "LayoutMac"); Text(samples, "Шкаф для примера", "Cabinet", "LayoutCabinet");
        Text(samples, "Считанный сканером код (сравнить)", "ScannedCode", "LayoutScannedCode");
        var command = new TextBox { Name = "LayoutCommand", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 140 };
        command.Bind(TextBox.TextProperty, new Binding("Command"));
        var diagnostics = new SelectableTextBlock { Name = "LabelBarcodeDiagnostics", TextWrapping = TextWrapping.Wrap };
        diagnostics.Bind(SelectableTextBlock.TextProperty, new Binding("Diagnostics"));
        var dataPanel = new StackPanel { Spacing = 8 };
        dataPanel.Children.Add(new Expander { Header = "Данные для примера и сканер", Content = samples, HorizontalAlignment = HorizontalAlignment.Stretch });
        dataPanel.Children.Add(diagnostics);
        var tabs = new TabControl { Items = { new TabItem { Header = "Данные и проверка", Content = new ScrollViewer { Content = dataPanel } },
            new TabItem { Header = "Команда принтера", Content = command } } };
        Grid.SetRow(tabs, 1); center.Children.Add(tabs);
        Grid.SetColumn(center, 2); main.Children.Add(center);
        var fields = new StackPanel { Spacing = 7 };
        fields.Bind(IsEnabledProperty, new Binding("HasSelection"));
        Text(fields, "Название элемента", "ElementName", "LayoutElementName");
        Combo(fields, "Тип элемента", "Kind", LabelLayoutEditorViewModel.Kinds.Cast<object>().ToArray());
        var visible = new CheckBox { Content = "Печатать элемент" }; visible.Bind(CheckBox.IsCheckedProperty, new Binding("Visible") { Mode = BindingMode.TwoWay }); fields.Children.Add(visible);
        Numeric(fields, "X от левого края, мм", "X", 0, 850); Numeric(fields, "Y сверху, мм", "Y", 0, 2500);
        Numeric(fields, "Ширина поля, мм", "Width", .08m, 850); Numeric(fields, "Высота / шрифт, мм", "Height", .08m, 2500);
        Numeric(fields, "Ширина символа ZPL, мм", "FontWidth", .08m, 85, "HasTextOptions");
        Numeric(fields, "Узкая полоса, точек", "Module", 1, 10, "HasBarcodeOptions"); Numeric(fields, "Толщина рамки, точек", "Stroke", 1, 100, "HasBoxOptions");
        var fit = new CheckBox { Content = "Подгонять текст по ширине" }; fit.Bind(CheckBox.IsCheckedProperty, new Binding("FitText") { Mode = BindingMode.TwoWay }); fields.Children.Add(fit);
        fit.Bind(IsVisibleProperty, new Binding("HasTextOptions"));
        Combo(fields, "Выравнивание", "SelectedAlignment", LabelLayoutEditorViewModel.Alignments.Cast<object>().ToArray(), "HasTextOptions");
        var fieldPicker = new ComboBox { Name = "LabelFieldPicker", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] {
            new Field("Название изделия", "{{Name}}"), new Field("Полный серийный номер", "{{Serial}}"), new Field("Короткий номер", "{{ShortSerial}}"),
            new Field("MAC", "{{Mac}}"), new Field("Составной штрихкод изделия", "{{Barcode}}"), new Field("Шкаф", "{{Cabinet}}"), new Field("Комплектация", "{{Variant}}") } };
        var valueLabel = new TextBlock { Text = "Вставить данные" }; valueLabel.Bind(IsVisibleProperty, new Binding("HasValue")); fields.Children.Add(valueLabel); fields.Children.Add(fieldPicker);
        fieldPicker.Bind(IsVisibleProperty, new Binding("HasValue"));
        fieldPicker.SelectionChanged += (_, _) => { if (fieldPicker.SelectedItem is Field f) { Editor.Value = f.Value; fieldPicker.SelectedItem = null; } };
        Text(fields, "Текст / формат (например, SN: {{Serial}})", "Value", "LabelElementValue", "HasValue");
        var inspector = new ScrollViewer { Content = fields }; Grid.SetColumn(inspector, 4); main.Children.Add(inspector);
        root.Children.Add(main); Content = root;

        bool HasInputErrors() => inputs.SelectMany(c => c.GetVisualDescendants().OfType<Control>().Prepend(c))
            .Any(c => c.IsEffectivelyVisible && DataValidationErrors.GetHasErrors(c));
        void Reorder(int delta) { if (Editor.Selected == null) return; var index = Editor.Elements.IndexOf(Editor.Selected); var next = Math.Clamp(index + delta, 0, Editor.Elements.Count - 1); Editor.Elements.Move(index, next); Editor.Refresh(); }
        void Text(Panel panel, string label, string property, string name, string? visibility = null)
        {
            var box = new TextBox { Name = name, MinWidth = 140 }; box.Bind(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay }); inputs.Add(box);
            var stack = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 8, 4) }; if (panel is WrapPanel) stack.Width = 190;
            if (visibility != null) stack.Bind(IsVisibleProperty, new Binding(visibility));
            stack.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }); stack.Children.Add(box); panel.Children.Add(stack);
        }
        void Numeric(Panel panel, string label, string property, decimal min, decimal max, string? visibility = null)
        {
            var integer = property is "Module" or "Stroke" or "Darkness";
            var box = new NumericUpDown { Name = "Layout" + property, Minimum = min, Maximum = max, Increment = integer ? 1 : .1m, FormatString = integer ? "0" : "0.###", MinWidth = 100 };
            box.Bind(NumericUpDown.ValueProperty, new Binding(property) { Mode = BindingMode.TwoWay }); inputs.Add(box);
            var stack = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 8, 4) }; if (visibility != null) stack.Bind(IsVisibleProperty, new Binding(visibility)); stack.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }); stack.Children.Add(box); panel.Children.Add(stack);
        }
        void Combo(Panel panel, string label, string property, object[] choices, string? visibility = null)
        {
            var box = new ComboBox { ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch }; box.Bind(ComboBox.SelectedItemProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            var stack = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 8, 4) }; if (visibility != null) stack.Bind(IsVisibleProperty, new Binding(visibility)); stack.Children.Add(new TextBlock { Text = label }); stack.Children.Add(box); panel.Children.Add(stack);
        }
    }
    private sealed record Field(string Title, string Value) { public override string ToString() => Title; }
}
