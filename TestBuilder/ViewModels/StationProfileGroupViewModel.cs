using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public partial class StationProfileGroupViewModel : ViewModelBase
{
    public string ModelName { get; }
    public ObservableCollection<StationProfileOptionViewModel> Profiles { get; } = new();
    [ObservableProperty] private bool isExpanded = true;

    public StationProfileGroupViewModel(string modelName) => ModelName = modelName;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}

public partial class StationProfileOptionViewModel : ViewModelBase
{
    public GraphProfile Profile { get; }
    public IRelayCommand SelectCommand { get; }
    [ObservableProperty] private bool isSelected;

    public StationProfileOptionViewModel(GraphProfile profile, Action<GraphProfile> select)
    {
        Profile = profile;
        SelectCommand = new RelayCommand(() => select(Profile));
    }
}
