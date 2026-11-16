using Avalonia.Controls;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

internal static class ProfileMenuItems
{
    internal static MenuItem Current(TestViewModel vm, GraphProfile profile) => new()
    {
        Header = "Актуальный",
        ToggleType = MenuItemToggleType.CheckBox,
        IsChecked = profile.IsMarkedCurrent,
        Command = vm.ToggleProfileCurrentCommand,
        CommandParameter = profile
    };
}
