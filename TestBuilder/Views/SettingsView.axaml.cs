using Avalonia.Controls;

namespace TestBuilder.Views
{
    public partial class SettingsView : UserControl
    {
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
        }

        private async void OnOpenTutorial(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is Window owner) await TutorialLauncher.StartAsync(owner);
        }
    }
}
