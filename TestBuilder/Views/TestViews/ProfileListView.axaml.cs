using Avalonia.Controls;
using Avalonia.Input;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views.TestViews;

public partial class ProfileListView : UserControl
{
    private GraphProfile? _profileBeforeClick;

    public ProfileListView()
    {
        InitializeComponent();

        ProfileListBox.AddHandler(
            PointerPressedEvent,
            OnProfileListPointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            handledEventsToo: false);

        ProfileListBox.AddHandler(
            PointerReleasedEvent,
            OnProfileListPointerReleased,
            Avalonia.Interactivity.RoutingStrategies.Bubble,
            handledEventsToo: false);
    }

    private void OnProfileListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _profileBeforeClick = e.GetCurrentPoint(ProfileListBox).Properties.IsLeftButtonPressed &&
            DataContext is TestViewModel vm ? vm.SelectedProfile : null;
    }

    private void OnProfileListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var profileBeforeClick = _profileBeforeClick;
        _profileBeforeClick = null;
        if (e.InitialPressMouseButton != MouseButton.Left || profileBeforeClick == null ||
            DataContext is not TestViewModel vm) return;

        var visual = ProfileListBox.InputHitTest(e.GetPosition(ProfileListBox));
        var element = visual as Avalonia.Controls.Control;
        GraphProfile? clicked = null;

        while (element != null)
        {
            if (element.DataContext is GraphProfile p)
            {
                clicked = p;
                break;
            }
            element = element.Parent as Avalonia.Controls.Control;
        }

        if (clicked == null) return;

        if (ReferenceEquals(clicked, profileBeforeClick) && ReferenceEquals(clicked, vm.SelectedProfile))
        {
            vm.ShowSelectedProfileGraph();
        }
    }
}
