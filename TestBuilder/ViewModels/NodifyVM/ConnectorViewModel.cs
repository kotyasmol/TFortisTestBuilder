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

        private string title = string.Empty;
        public string Title
        {
            get => title;
            set { if (SetProperty(ref title, value)) OnPropertyChanged(nameof(DisplayTitle)); }
        }
        public string DisplayTitle => TestBuilder.Presentation.RussianUiText.Display(Title);

        public NodeViewModel? Parent { get; set; }
    }
}