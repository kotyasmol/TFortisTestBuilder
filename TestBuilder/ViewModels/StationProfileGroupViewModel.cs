using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public partial class StationProfileGroupViewModel : ViewModelBase
{
    public string ModelName { get; }
    public string Key { get; }
    public ObservableCollection<StationProfileOptionViewModel> Profiles { get; } = new();
    public IEnumerable<StationProfileOptionViewModel> VisibleProfiles => IsExpanded ? Profiles : Profiles.Where(p => p.Profile.IsLatest);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleProfiles))]
    private bool isExpanded;

    public StationProfileGroupViewModel(string modelName, string? key = null) { ModelName = modelName; Key = key ?? modelName; }
    internal void RefreshVisibleProfiles() => OnPropertyChanged(nameof(VisibleProfiles));

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
