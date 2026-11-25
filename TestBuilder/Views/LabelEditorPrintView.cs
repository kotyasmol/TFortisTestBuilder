using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

internal sealed class LabelEditorPrintView : UserControl
{
    public LabelEditorPrintView(LabelLayoutEditorViewModel editor, Func<bool> canUse)
    {
        var panel = new StackPanel { Spacing = 6 };
        var printer = new PrinterPicker { PrinterName = AppSettings.Instance.DefaultPrinterName ?? "", HorizontalAlignment = HorizontalAlignment.Stretch };
        var button = new Button { Content = "Пробная печать: 1 шт.", Name = "PrintInlineLabelSample" };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        panel.Children.Add(printer); panel.Children.Add(button); panel.Children.Add(status); Content = panel;
        CancellationTokenSource? printing = null;
        button.Click += async (_, _) =>
        {
            if (printing != null) return;
            TestViewModel? locked = null;
            try
            {
                if (!canUse()) throw new InvalidOperationException("Пробная печать доступна после завершения текущей операции.");
                var parent = this.FindAncestorOfType<LabelTemplateEditorView>();
                if (parent?.HasInputErrors() == true) throw new InvalidOperationException("Исправьте поля этикетки.");
                var template = editor.Apply(); template.CabinetName = editor.Cabinet;
                if (string.IsNullOrWhiteSpace(printer.PrinterName)) throw new InvalidOperationException("Выберите принтер.");
                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.DataContext is MainWindowViewModel main)
                { var test = main.TestVM; if (test.IsLabelToolBusy || test.IsNetworkSetupBusy) throw new InvalidOperationException("Дождитесь завершения текущей операции."); locked = test; locked.IsLabelToolBusy = true; }
                printing = new(); button.IsEnabled = false; if (parent != null) parent.IsEnabled = false; status.Text = "Пробная печать…";
                try
                {
                    var step = new PrintLabelStep(LoggingService.Instance.CreateLogger("Пробная этикетка"), printer.PrinterName, "SerialNumber", "SerialShort", "Mac", true,
                        editor.Serial, editor.Mac, 1, template.UseShortSerial, true, WindowsRawLabelPrinter.Shared, 30000, template: template, preserveTemplate: true);
                    var context = new TestContext(new RegisterState());
                    var result = await step.ExecuteAsync(context, printing.Token);
                    status.Text = result == StepResult.False ? context.GetVariable<string>("PrintLabel.Error") : "Одна этикетка передана принтеру.";
                }
                finally { if (parent != null) parent.IsEnabled = true; }
            }
            catch (OperationCanceledException) { status.Text = "Пробная печать отменена."; }
            catch (Exception ex) when (LabelTypesViewModel.IsCatalogError(ex)) { status.Text = ex.Message; }
            finally { printing?.Dispose(); printing = null; button.IsEnabled = true; if (locked != null) locked.IsLabelToolBusy = false; }
        };
        DetachedFromVisualTree += (_, _) => printing?.Cancel();
    }
}
