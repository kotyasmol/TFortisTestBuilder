using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TestBuilder.Views
{
    public partial class SettingsView : UserControl
    {
        private Window? _printerSettingsOwner;

        // The engineer tab and station settings share a VM, but not radio-button groups.
        public string WorkspaceModeGroupName { get; } = System.Guid.NewGuid().ToString();
        public string ThemeGroupName { get; } = System.Guid.NewGuid().ToString();

        private void OnOpenUserGuide(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var guide = new UserGuideWindow();
            if (TopLevel.GetTopLevel(this) is Window owner) guide.Show(owner);
            else guide.Show();
        }

        public SettingsView()
        {
            InitializeComponent();
            WindowsPrinterManagement.IsVisible = OperatingSystem.IsWindows();
        }

        private async void OnOpenWindowsPrinters(object? sender, RoutedEventArgs e)
        {
            if (!OperatingSystem.IsWindows() || sender is not Button button ||
                TopLevel.GetTopLevel(this) is not Window owner) return;

            button.IsEnabled = false;
            WindowsPrinterSettingsError.IsVisible = false;
            StopWaitingForPrinterSettings();
            _printerSettingsOwner = owner;
            owner.Activated += OnReturnedFromPrinterSettings;
            try
            {
                if (await owner.Launcher.LaunchUriAsync(new Uri("ms-settings:printers"))) return;
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceWarning("Could not open Windows printer settings: {0}", error.Message);
            }
            finally { button.IsEnabled = true; }

            StopWaitingForPrinterSettings();
            WindowsPrinterSettingsError.IsVisible = true;
        }

        private async void OnReturnedFromPrinterSettings(object? sender, EventArgs e)
        {
            StopWaitingForPrinterSettings();
            if (!IsEffectivelyVisible) return;
            var refresh = DefaultPrinterPicker.State.RefreshCommand;
            if (refresh.IsRunning && refresh.ExecutionTask is { } pending) await pending;
            if (IsEffectivelyVisible && ReferenceEquals(TopLevel.GetTopLevel(this), sender) && refresh.CanExecute(null))
                await refresh.ExecuteAsync(null);
        }

        private void StopWaitingForPrinterSettings()
        {
            if (_printerSettingsOwner == null) return;
            _printerSettingsOwner.Activated -= OnReturnedFromPrinterSettings;
            _printerSettingsOwner = null;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            StopWaitingForPrinterSettings();
            base.OnDetachedFromVisualTree(e);
        }

        private async void OnOpenTutorial(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is Window owner) await TutorialLauncher.StartAsync(owner);
        }
    }
}
