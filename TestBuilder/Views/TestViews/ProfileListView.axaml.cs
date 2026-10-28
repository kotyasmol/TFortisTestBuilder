using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Linq;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views.TestViews;

public partial class ProfileListView : UserControl
{
    private GraphProfile? _profileBeforeClick;
    private bool _syncingSelection;
    internal ContextMenu? ProfileMenu { get; private set; }

    public ProfileListView()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => SyncSelection();

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

    private void OnGroupSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || DataContext is not TestViewModel vm || sender is not ListBox list || list.SelectedItem is not GraphProfile profile) return;
        vm.SelectedProfile = profile;
        SyncSelection();
    }

    private void SyncSelection()
    {
        if (DataContext is not TestViewModel vm) return;
        _syncingSelection = true;
        try
        {
            foreach (var list in this.GetVisualDescendants().OfType<ListBox>())
                list.SelectedItem = vm.SelectedProfile == null ? null : list.Items.OfType<GraphProfile>().FirstOrDefault(p => p.FilePath == vm.SelectedProfile.FilePath);
        }
        finally { _syncingSelection = false; }
    }

    private void OnProfileListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(ProfileListBox).Properties.IsRightButtonPressed && DataContext is TestViewModel vm)
        {
            _profileBeforeClick = null;
            var element = ProfileListBox.InputHitTest(e.GetPosition(ProfileListBox)) as Control;
            while (element != null)
            {
                if (element.DataContext is GraphProfile profile)
                {
                    ProfileMenu?.Close();
                    ProfileMenu = new ContextMenu
                    {
                        ItemsSource = new[]
                        {
                            new MenuItem { Header = "Копировать профиль", Command = vm.CopyProfileCommand, CommandParameter = profile },
                            new MenuItem { Header = "Переименовать профиль", Command = vm.RenameProfileCommand, CommandParameter = profile }
                        }
                    };
                    ProfileMenu.Open(element);
                    e.Handled = true;
                    return;
                }
                element = element.Parent as Control;
            }
            return;
        }
        _profileBeforeClick = e.GetCurrentPoint(ProfileListBox).Properties.IsLeftButtonPressed &&
            DataContext is TestViewModel current ? current.SelectedProfile : null;
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
