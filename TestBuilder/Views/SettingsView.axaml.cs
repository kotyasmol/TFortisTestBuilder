using Avalonia.Controls;

namespace TestBuilder.Views
{
    public partial class SettingsView : UserControl
    {
        // The engineer tab and station settings share a VM, but not radio-button groups.
        public string WorkspaceModeGroupName { get; } = System.Guid.NewGuid().ToString();
        public string ThemeGroupName { get; } = System.Guid.NewGuid().ToString();

        public SettingsView()
        {
            InitializeComponent();
        }
    }
}
