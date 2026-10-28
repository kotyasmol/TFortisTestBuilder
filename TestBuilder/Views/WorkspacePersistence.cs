using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

/// <summary>Owned by the real main window, so tutorials and mini-graph editors stay isolated.</summary>
internal sealed class WorkspacePersistence : IDisposable
{
    private readonly MainWindow _window;
    private readonly MainWindowViewModel _vm;
    private readonly WorkspacePreferences _state;
    private readonly Action _save;
    private readonly DispatcherTimer _saveTimer;
    private readonly List<Action> _unsubscribe = new();
    private readonly Action _restoreEngineerGroups;
    private readonly Action _restoreStationGroups;
    private readonly Grid _logLayout;
    private readonly Grid _boardLayout;
    private bool _restoring;
    private bool _dirty;
    private bool _opened;
    private bool _reportedSaveError;

    public WorkspacePersistence(MainWindow window, AppSettings settings, Action save)
    {
        _window = window;
        _vm = (MainWindowViewModel)window.DataContext!;
        _state = settings.Workspace ??= new WorkspacePreferences();
        _state.Normalize();
        _save = save;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => Flush();

        var test = _vm.TestVM;
        test.ProductFilter = _state.ProductFilter switch { "Board" => "Платы", "Switch" => "Коммутаторы", _ => "Все изделия" };
        test.IsProfilesCollapsed = _state.ProfilesCollapsed;
        test.IsPaletteCollapsed = _state.PaletteCollapsed;
        test.IsQuickActionsCollapsed = _state.QuickActionsCollapsed;
        _vm.SelectPageCommand.Execute(_state.EngineerPage);

        _restoreEngineerGroups = WatchGroups(test.EngineerProfileGroups, g => "profiles/engineer/" + g.Key,
            g => g.IsExpanded, (g, value) => g.IsExpanded = value, _ => false,
            () => !string.IsNullOrWhiteSpace(test.ProfileSearch));
        _restoreStationGroups = WatchGroups(test.StationProfileGroups, g => "profiles/operator/" + g.Key,
            g => g.IsExpanded, (g, value) => g.IsExpanded = value, _ => false,
            () => !string.IsNullOrWhiteSpace(test.StationProfileSearch));
        WatchGroups(test.AvailableNodeCategories, g => "palette/" + g.Title,
            g => g.IsExpanded, (g, value) => g.IsExpanded = value, _ => true, () => false);
        WatchGroups(_vm.SelfTestPageVM.Categories, g => "selftest/" + g.Key,
            g => g.IsExpanded, (g, value) => g.IsExpanded = value, g => g.DefaultIsExpanded, () => false);
        var network = window.FindControl<NetworkSetupView>("NetworkSetupViewControl")!;
        WatchExpander(network.FindControl<Expander>("IdentifyCableSection")!, "network/cable");
        WatchExpander(network.FindControl<Expander>("NetworkJournalSection")!, "network/journal");

        var editor = window.FindControl<TestView>("TestViewControl")!;
        _logLayout = editor.FindControl<Grid>("EditorLogLayout")!;
        _boardLayout = window.FindControl<StationDashboardView>("StationViewControl")!.FindControl<Grid>("BoardWorkspace")!;
        _logLayout.RowDefinitions[2].Height = new GridLength(_state.LogHeight);
        _boardLayout.ColumnDefinitions[2].Width = new GridLength(_state.BoardStagesWidth);
        _logLayout.RowDefinitions[2].PropertyChanged += OnLogHeightChanged;
        _boardLayout.ColumnDefinitions[2].PropertyChanged += OnBoardWidthChanged;
        _logLayout.SizeChanged += OnLayoutSizeChanged;
        _boardLayout.SizeChanged += OnLayoutSizeChanged;

        window.Width = _state.WindowWidth;
        window.Height = _state.WindowHeight;
        if (_state.WindowMaximized) window.WindowState = WindowState.Maximized;
        window.Opened += OnOpened;
        window.PropertyChanged += OnWindowPropertyChanged;
        test.PropertyChanged += OnTestPropertyChanged;
        _vm.PropertyChanged += OnShellPropertyChanged;
    }

    // Search expands matches temporarily; it must not replace the user's saved grouping.
    private Action WatchGroups<T>(ObservableCollection<T> groups, Func<T, string> key, Func<T, bool> read,
        Action<T, bool> apply, Func<T, bool> defaultValue, Func<bool> searching) where T : INotifyPropertyChanged
    {
        var watched = new HashSet<T>();
        void Restore(T group)
        {
            if (searching()) return;
            var wasRestoring = _restoring;
            _restoring = true;
            try { apply(group, _state.ExpandedSections.GetValueOrDefault(key(group), defaultValue(group))); }
            finally { _restoring = wasRestoring; }
        }
        void Attach()
        {
            foreach (var group in groups)
            {
                if (!watched.Add(group)) continue;
                Restore(group);
                PropertyChangedEventHandler handler = (_, e) =>
                {
                    if (_restoring || searching() || e.PropertyName != "IsExpanded") return;
                    _state.ExpandedSections[key(group)] = read(group);
                    SaveSoon();
                };
                group.PropertyChanged += handler;
                _unsubscribe.Add(() => group.PropertyChanged -= handler);
            }
        }
        Attach();
        NotifyCollectionChangedEventHandler changed = (_, _) => Attach();
        groups.CollectionChanged += changed;
        _unsubscribe.Add(() => groups.CollectionChanged -= changed);
        return () => { foreach (var group in groups) Restore(group); };
    }

    private void WatchExpander(Expander expander, string key)
    {
        expander.IsExpanded = _state.ExpandedSections.GetValueOrDefault(key);
        EventHandler<AvaloniaPropertyChangedEventArgs> changed = (_, e) =>
        {
            if (e.Property != Expander.IsExpandedProperty) return;
            _state.ExpandedSections[key] = expander.IsExpanded;
            SaveSoon();
        };
        expander.PropertyChanged += changed;
        _unsubscribe.Add(() => expander.PropertyChanged -= changed);
    }

    private void OnTestPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var test = _vm.TestVM;
        switch (e.PropertyName)
        {
            case nameof(test.ProductFilter):
                _state.ProductFilter = test.ProductFilter switch { "Платы" => "Board", "Коммутаторы" => "Switch", _ => "All" };
                break;
            case nameof(test.IsProfilesCollapsed): _state.ProfilesCollapsed = test.IsProfilesCollapsed; break;
            case nameof(test.IsPaletteCollapsed): _state.PaletteCollapsed = test.IsPaletteCollapsed; break;
            case nameof(test.IsQuickActionsCollapsed): _state.QuickActionsCollapsed = test.IsQuickActionsCollapsed; break;
            case nameof(test.ProfileSearch): _restoreEngineerGroups(); return;
            case nameof(test.StationProfileSearch): _restoreStationGroups(); return;
            default: return;
        }
        SaveSoon();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(_vm.SelectedPage)) return;
        _state.EngineerPage = _vm.SelectedPage;
        SaveSoon();
    }

    private void OnLogHeightChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_restoring || e.Property != RowDefinition.HeightProperty) return;
        var height = _logLayout.RowDefinitions[2].Height;
        if (!height.IsAbsolute || !double.IsFinite(height.Value)) return;
        _state.LogHeight = Math.Max(100, height.Value);
        SaveSoon();
    }

    private void OnBoardWidthChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_restoring || e.Property != ColumnDefinition.WidthProperty) return;
        var width = _boardLayout.ColumnDefinitions[2].Width;
        if (!width.IsAbsolute || !double.IsFinite(width.Value)) return;
        _state.BoardStagesWidth = Math.Max(220, width.Value);
        SaveSoon();
    }

    private void OnLayoutSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _restoring = true;
        try
        {
            // Clamp the visible pane on a smaller screen, retaining the preferred size for later.
            if (_logLayout.Bounds.Height >= 272)
                _logLayout.RowDefinitions[2].Height = new GridLength(Math.Clamp(_state.LogHeight, 100, _logLayout.Bounds.Height - 172));
            if (_boardLayout.Bounds.Width >= 472)
                _boardLayout.ColumnDefinitions[2].Width = new GridLength(Math.Clamp(_state.BoardStagesWidth, 220, _boardLayout.Bounds.Width - 252));
        }
        finally { _restoring = false; }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Normal && _window.Screens.ScreenFromWindow(_window) is { } screen)
        {
            _window.Width = Math.Clamp(_window.Width, _window.MinWidth, Math.Max(_window.MinWidth, screen.WorkingArea.Width / screen.Scaling));
            _window.Height = Math.Clamp(_window.Height, _window.MinHeight, Math.Max(_window.MinHeight, screen.WorkingArea.Height / screen.Scaling));
        }
        _opened = true;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_opened) return;
        if (e.Property == Window.WindowStateProperty && _window.WindowState != WindowState.Minimized)
        {
            _state.WindowMaximized = _window.WindowState == WindowState.Maximized;
            SaveSoon();
        }
        if (e.Property == Control.BoundsProperty && _window.WindowState == WindowState.Normal &&
            _window.Bounds.Width >= _window.MinWidth && _window.Bounds.Height >= _window.MinHeight)
        {
            _state.WindowWidth = _window.Bounds.Width;
            _state.WindowHeight = _window.Bounds.Height;
            SaveSoon();
        }
    }

    private void SaveSoon()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    internal void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        try { _save(); _dirty = false; _reportedSaveError = false; }
        catch (Exception ex)
        {
            if (!_reportedSaveError) _vm.TestVM.TestingLogger.Warning("Не удалось сохранить настройки интерфейса: " + ex.Message);
            _reportedSaveError = true;
        }
    }

    public void Dispose()
    {
        Flush();
        _window.Opened -= OnOpened;
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _vm.TestVM.PropertyChanged -= OnTestPropertyChanged;
        _vm.PropertyChanged -= OnShellPropertyChanged;
        _logLayout.RowDefinitions[2].PropertyChanged -= OnLogHeightChanged;
        _boardLayout.ColumnDefinitions[2].PropertyChanged -= OnBoardWidthChanged;
        _logLayout.SizeChanged -= OnLayoutSizeChanged;
        _boardLayout.SizeChanged -= OnLayoutSizeChanged;
        foreach (var unsubscribe in _unsubscribe) unsubscribe();
    }
}
