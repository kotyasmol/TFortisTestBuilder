using System;
using Avalonia;
using Avalonia.Controls;

namespace TestBuilder.Views;

public partial class RegisterIndicatorsView : UserControl
{
    public static readonly StyledProperty<double> WorkspaceHeightProperty =
        AvaloniaProperty.Register<RegisterIndicatorsView, double>(nameof(WorkspaceHeight), double.NaN);

    public double WorkspaceHeight
    {
        get => GetValue(WorkspaceHeightProperty);
        set => SetValue(WorkspaceHeightProperty, value);
    }

    public RegisterIndicatorsView()
    {
        InitializeComponent();
        MaxHeight = 320;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Keep at least half of the workspace for the graph/stages, even with many cards.
        if (change.Property == WorkspaceHeightProperty && double.IsFinite(WorkspaceHeight) && WorkspaceHeight > 0)
            MaxHeight = Math.Min(420, WorkspaceHeight / 2);
    }
}
