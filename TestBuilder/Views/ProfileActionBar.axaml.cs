using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TestBuilder.Views;
public partial class ProfileActionBar : UserControl
{
    public static readonly StyledProperty<bool> IsRightDockedProperty =
        AvaloniaProperty.Register<ProfileActionBar, bool>(nameof(IsRightDocked));

    public bool IsRightDocked
    {
        get => GetValue(IsRightDockedProperty);
        set => SetValue(IsRightDockedProperty, value);
    }

    public ProfileActionBar()
    {
        InitializeComponent();
        UpdatePlacement();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsRightDockedProperty) UpdatePlacement();
    }

    private void UpdatePlacement()
    {
        if (this.FindControl<Grid>("QuickActionsLayout") is not { } layout) return;
        layout.ColumnDefinitions = new ColumnDefinitions(IsRightDocked ? "24,Auto" : "Auto,24");
        Grid.SetColumn(QuickActionsPanel, IsRightDocked ? 1 : 0);
        Grid.SetColumn(QuickActionsHandle, IsRightDocked ? 0 : 1);
        QuickActionsPanel.BorderThickness = IsRightDocked ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        QuickActionsArrows.RenderTransform = new ScaleTransform(IsRightDocked ? -1 : 1, 1);
    }
}
