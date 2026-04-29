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

    private void OnProfileSelectionChanged(object? sender, SelectionChangedEventArgs e) { }

    private void OnProfileListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;
        _profileBeforeClick = vm.SelectedProfile;
    }

    private void OnProfileListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;
        if (_profileBeforeClick == null) return;

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

        if (ReferenceEquals(clicked, _profileBeforeClick))
        {
            var name = clicked.Name;
            _profileBeforeClick = null;
            ProfileListBox.SelectedItem = null;
            vm.ClearGraph();
            vm.StatusMessage = $"Профиль закрыт: {name}";
        }
    }
}
