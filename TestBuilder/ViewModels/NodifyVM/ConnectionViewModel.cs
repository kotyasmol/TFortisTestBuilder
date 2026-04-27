using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestBuilder.ViewModels.NodifyVM
{
    public partial class ConnectionViewModel : ObservableObject
    {
        private static readonly IBrush DefaultStrokeBrush =
            new SolidColorBrush(Color.FromRgb(120, 120, 130));

        private static readonly IBrush SelectedStrokeBrush =
            new SolidColorBrush(Color.FromRgb(255, 214, 10));

        private static readonly IBrush SelectedOutlineBrush =
            new SolidColorBrush(Color.FromRgb(255, 214, 10), 0.35);

        public ConnectionViewModel(
            ConnectorViewModel source,
            ConnectorViewModel target)
        {
            Source = source;
            Target = target;

            Source.IsConnected = true;
            Target.IsConnected = true;
        }

        public ConnectorViewModel Source { get; }

        public ConnectorViewModel Target { get; }

        [ObservableProperty]
        private bool isSelected;

        public IBrush StrokeBrush =>
            IsSelected ? SelectedStrokeBrush : DefaultStrokeBrush;

        public double StrokeThickness =>
            IsSelected ? 4 : 2;

        public IBrush? OutlineBrush =>
            IsSelected ? SelectedOutlineBrush : null;

        public double OutlineThickness =>
            IsSelected ? 4 : 0;

        partial void OnIsSelectedChanged(bool value)
        {
            OnPropertyChanged(nameof(StrokeBrush));
            OnPropertyChanged(nameof(StrokeThickness));
            OnPropertyChanged(nameof(OutlineBrush));
            OnPropertyChanged(nameof(OutlineThickness));
        }
    }
}