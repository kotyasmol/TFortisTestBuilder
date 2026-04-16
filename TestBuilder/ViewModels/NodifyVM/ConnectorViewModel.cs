using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels
{
    public partial class ConnectorViewModel : ObservableObject
    {
        [ObservableProperty]
        private Point anchor;

        [ObservableProperty]
        private bool isConnected;

        public string Title { get; set; } = string.Empty;

        public NodeViewModel? Parent { get; set; }
    }
}