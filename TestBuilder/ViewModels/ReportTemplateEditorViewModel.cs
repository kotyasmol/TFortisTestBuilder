using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;

namespace TestBuilder.ViewModels;

public partial class ReportTemplateEditorViewModel : ObservableObject
{
    private static readonly ReportField TestTypeField = new() { Source = ReportSourceKind.TestType };
    private static readonly TestReportEntry[] NoPreview = [];

    public ObservableCollection<ReportFieldEditorViewModel> Rows { get; } = new();
    public bool HasSelection => SelectedRow != null;
    public bool CanAddTestType => Rows.All(row => !row.IsTestType);
    public bool CanDelete => SelectedRow is { IsTestType: false };
    public bool CanMoveUp => SelectedRow != null && Rows.IndexOf(SelectedRow) > 0;
    public bool CanMoveDown => SelectedRow != null && Rows.IndexOf(SelectedRow) < Rows.Count - 1;
    public bool HasVisibleVariables => VisibleVariables.Count != 0;
    public bool HasVariables => _variables.Length != 0;
    public bool HasNoVariableMatches => HasVariables && !HasVisibleVariables;
    public string VariableSummary => $"Отмечено {_variables.Count(variable => variable.IsIncluded)} из {_variables.Length}";

    [ObservableProperty] private bool useTemplate;
    [ObservableProperty] private bool includeAutomaticEntries;
    [ObservableProperty] private ReportFieldEditorViewModel? selectedRow;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private IReadOnlyList<TestReportEntry> preview = NoPreview;
    [ObservableProperty] private string variableSearch = "";
    [ObservableProperty] private IReadOnlyList<ReportVariableChoiceViewModel> visibleVariables = [];

    private readonly Func<ReportSourceKind, IReadOnlyList<string>> _getKnownKeys;
    private readonly IReadOnlyList<string> _knownVariableKeys;
    private ReportVariableChoiceViewModel[] _variables = [];
    private bool _applyingChecklist;
    private bool _includeKnownVariablesOnFirstEnable;

    public ReportTemplateEditorViewModel(ReportTemplate? source,
        Func<ReportSourceKind, IReadOnlyList<string>>? getKnownKeys = null,
        bool initiallyIncludeKnownVariables = false)
    {
        _getKnownKeys = getKnownKeys ?? (_ => []);
        _knownVariableKeys = _getKnownKeys(ReportSourceKind.Variable);
        useTemplate = source != null;
        includeAutomaticEntries = source?.IncludeAutomaticEntries ?? true;
        _includeKnownVariablesOnFirstEnable = source == null && initiallyIncludeKnownVariables;
        foreach (var field in source?.Fields ?? [TestTypeField])
            Append(field);
        SelectedRow = Rows.FirstOrDefault();
        RefreshRows();
    }

    public void AddRow()
    {
        if (!UseTemplate) return;
        var row = Append(new ReportField { Source = ReportSourceKind.Variable });
        SelectedRow = row;
        RefreshRows();
    }

    public void AddTestType()
    {
        if (!UseTemplate || !CanAddTestType) return;
        SelectedRow = Append(TestTypeField);
        RefreshRows();
    }

    public void DeleteSelected()
    {
        if (!UseTemplate || SelectedRow is not { IsTestType: false } row) return;
        var index = Rows.IndexOf(row);
        if (index < 0) return;
        row.PropertyChanged -= OnRowChanged;
        Rows.RemoveAt(index);
        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
        RefreshRows();
    }

    public void MoveSelected(int delta)
    {
        if (!UseTemplate || SelectedRow is not { } row) return;
        var from = Rows.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Rows.Count || from == to) return;
        Rows.Move(from, to);
        SelectedRow = row;
        RefreshRows();
    }

    public void SetVisibleVariablesIncluded(bool included) => SetVariablesIncluded(VisibleVariables, included);

    private void SetVariablesIncluded(IEnumerable<ReportVariableChoiceViewModel> variables, bool included)
    {
        if (!UseTemplate) return;
        var changed = variables.Where(variable => variable.IsIncluded != included).ToArray();
        if (changed.Length == 0) return;

        var configured = Rows.Where(row => row.Source == ReportSourceKind.Variable && !string.IsNullOrWhiteSpace(row.Key))
            .ToLookup(row => row.Key.Trim(), StringComparer.Ordinal);
        var usedLabels = Rows.Select(row => row.DisplayLabel.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _applyingChecklist = true;
        try
        {
            foreach (var variable in changed)
            {
                if (!included)
                {
                    foreach (var row in configured[variable.Key]) row.Enabled = false;
                    continue;
                }

                if (configured[variable.Key].FirstOrDefault() is { } existing)
                {
                    existing.Enabled = true;
                }
                else
                {
                    Append(new ReportField
                    {
                        Source = ReportSourceKind.Variable,
                        Key = variable.Key,
                        Label = GetNewVariableLabel(variable.Key, usedLabels)
                    });
                }
            }
        }
        finally { _applyingChecklist = false; }
        SelectedRow ??= Rows.FirstOrDefault();
        RefreshRows();
    }

    private static string GetNewVariableLabel(string key, HashSet<string> usedLabels)
    {
        var label = ReportField.SanitizeWireText(key);
        if (usedLabels.Add(label)) return label == key ? "" : label;
        for (var index = 1; ; index++)
        {
            var alternative = index == 1 ? $"{label} (переменная)" : $"{label} (переменная {index})";
            if (usedLabels.Add(alternative)) return alternative;
        }
    }

    internal ReportTemplate? BuildResult()
    {
        if (!UseTemplate) return null;
        var result = new ReportTemplate
        {
            IncludeAutomaticEntries = IncludeAutomaticEntries,
            Fields = Rows.Select(row => row.ToField()).ToArray()
        };
        if (result.Validate() is { } error) throw new InvalidOperationException(error);
        return result;
    }

    private ReportFieldEditorViewModel Append(ReportField field)
    {
        var row = new ReportFieldEditorViewModel(field, _getKnownKeys);
        row.PropertyChanged += OnRowChanged;
        Rows.Add(row);
        return row;
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applyingChecklist) return;
        if (e.PropertyName is not (nameof(ReportFieldEditorViewModel.Enabled) or nameof(ReportFieldEditorViewModel.Label)
            or nameof(ReportFieldEditorViewModel.Key) or nameof(ReportFieldEditorViewModel.StatusKey)
            or nameof(ReportFieldEditorViewModel.Source) or nameof(ReportFieldEditorViewModel.Format))) return;
        Error = "";
        if (e.PropertyName is nameof(ReportFieldEditorViewModel.Enabled)
            or nameof(ReportFieldEditorViewModel.Key) or nameof(ReportFieldEditorViewModel.Source))
            RefreshVariables();
        RefreshPreview();
    }

    private void RefreshRows()
    {
        for (var i = 0; i < Rows.Count; i++) Rows[i].Position = i + 1;
        OnPropertyChanged(nameof(CanAddTestType));
        RefreshSelection();
        Error = "";
        RefreshVariables();
        RefreshPreview();
    }

    private void RefreshVariables()
    {
        var configured = Rows.Where(row => row.Source == ReportSourceKind.Variable && !string.IsNullOrWhiteSpace(row.Key))
            .ToArray();
        var included = configured.Where(row => row.Enabled).Select(row => row.Key.Trim()).ToHashSet(StringComparer.Ordinal);
        var keys = _knownVariableKeys.Concat(configured.Select(row => row.Key.Trim()))
            .Where(key => ReportTemplate.CanSelectVariable(key)).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (!keys.SequenceEqual(_variables.Select(variable => variable.Key), StringComparer.Ordinal))
        {
            var previous = _variables.ToDictionary(variable => variable.Key, StringComparer.Ordinal);
            _variables = keys.Select(key => previous.TryGetValue(key, out var variable) ? variable :
                new ReportVariableChoiceViewModel(key, (item, value) => SetVariablesIncluded([item], value))).ToArray();
            RefreshVisibleVariables();
        }
        foreach (var variable in _variables) variable.RefreshIncluded(included.Contains(variable.Key));
        OnPropertyChanged(nameof(HasVariables));
        OnPropertyChanged(nameof(VariableSummary));
    }

    private void RefreshVisibleVariables()
    {
        var search = VariableSearch?.Trim() ?? "";
        VisibleVariables = search.Length == 0
            ? _variables
            : _variables.Where(variable => variable.Key.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        OnPropertyChanged(nameof(HasVisibleVariables));
        OnPropertyChanged(nameof(HasNoVariableMatches));
    }

    private void RefreshPreview()
    {
        if (!UseTemplate)
        {
            Preview = NoPreview;
            return;
        }

        var preview = new List<TestReportEntry>();
        if (CanAddTestType) preview.Add(TestTypeField.GetPreview());
        preview.AddRange(Rows
            .Where(row => row.Enabled || row.IsTestType)
            .Select(row => row.ToField().GetPreview()));
        Preview = preview;
    }

    private void RefreshSelection()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    partial void OnSelectedRowChanged(ReportFieldEditorViewModel? value) => RefreshSelection();

    partial void OnVariableSearchChanged(string value) => RefreshVisibleVariables();

    partial void OnUseTemplateChanged(bool value)
    {
        if (value && _includeKnownVariablesOnFirstEnable)
        {
            _includeKnownVariablesOnFirstEnable = false;
            SetVariablesIncluded(_variables, true);
        }
        Error = "";
        RefreshPreview();
    }
}

public sealed class ReportVariableChoiceViewModel : ObservableObject
{
    private readonly Action<ReportVariableChoiceViewModel, bool> _changeSelection;
    private bool _isIncluded;

    internal ReportVariableChoiceViewModel(string key, Action<ReportVariableChoiceViewModel, bool> changeSelection)
    {
        Key = key;
        _changeSelection = changeSelection;
    }

    public string Key { get; }

    public bool IsIncluded
    {
        get => _isIncluded;
        set { if (_isIncluded != value) _changeSelection(this, value); }
    }

    internal void RefreshIncluded(bool included) => SetProperty(ref _isIncluded, included, nameof(IsIncluded));
}

public sealed record ReportFieldOption<T>(T Value, string Title)
{
    public override string ToString() => Title;
}

public partial class ReportFieldEditorViewModel : ObservableObject
{
    private readonly ReportField _sourceField;
    private readonly Func<ReportSourceKind, IReadOnlyList<string>> _getKnownKeys;

    private static readonly IReadOnlyList<ReportFieldOption<ReportSourceKind>> SourceChoices =
    [
        new(ReportSourceKind.Entry, "Строка проверки"),
        new(ReportSourceKind.Variable, "Переменная"),
        new(ReportSourceKind.AllPassed, "Все указанные проверки")
    ];

    private static readonly IReadOnlyList<ReportFieldOption<ReportValueFormat>> FormatChoices =
    [
        new(ReportValueFormat.Text, "Как есть"),
        new(ReportValueFormat.BooleanText, "true / false"),
        new(ReportValueFormat.BooleanNumber, "1 / 0"),
        new(ReportValueFormat.FixedSix, "Число с 6 знаками после точки"),
        new(ReportValueFormat.PoeMillivolts, "Напряжение PoE в мВ")
    ];
    private static readonly IReadOnlyList<ReportFieldOption<ReportValueFormat>> BooleanFormatChoices =
        FormatChoices.Take(3).ToArray();

    public IReadOnlyList<ReportFieldOption<ReportSourceKind>> SourceOptions => SourceChoices;
    public IReadOnlyList<ReportFieldOption<ReportValueFormat>> FormatOptions => Source == ReportSourceKind.AllPassed
        ? BooleanFormatChoices : FormatChoices;
    public IReadOnlyList<string> KnownKeys => _getKnownKeys(Source);
    public bool HasKnownKeys => KnownKeys.Count != 0;
    public IReadOnlyList<string> KnownStatusKeys => _getKnownKeys(ReportSourceKind.AllPassed);
    public bool IsTestType => Source == ReportSourceKind.TestType;
    public bool CanToggle => !IsTestType;
    public bool CanSetStatus => Source == ReportSourceKind.Variable;
    public bool IsPoeFormat => Format == ReportValueFormat.PoeMillivolts;
    public string StatusCaption => IsPoeFormat ? "Ключ результата проверки PoE" : "Ключ результата проверки (если есть)";
    public string DisplayLabel => IsTestType ? "Тип проверки" :
        !string.IsNullOrWhiteSpace(Label) ? Label : !string.IsNullOrWhiteSpace(Key) ? Key : "Новая строка";
    public string KeyCaption => Source switch
    {
        ReportSourceKind.Entry => "Точная подпись строки в результате проверки",
        ReportSourceKind.AllPassed => "Ключи результатов через точку с запятой",
        _ => "Ключ переменной, которую записывает нода"
    };

    [ObservableProperty] private bool enabled;
    [ObservableProperty] private ReportSourceKind source;
    [ObservableProperty] private string key;
    [ObservableProperty] private string label;
    [ObservableProperty] private ReportValueFormat format;
    [ObservableProperty] private string statusKey;
    [ObservableProperty] private int position;
    [ObservableProperty] private string? pickedKey;

    public ReportFieldEditorViewModel(ReportField field, Func<ReportSourceKind, IReadOnlyList<string>> getKnownKeys)
    {
        _sourceField = field;
        _getKnownKeys = getKnownKeys;
        enabled = field.Enabled || field.Source == ReportSourceKind.TestType;
        source = field.Source;
        key = field.Key;
        label = field.Label;
        format = field.Format;
        statusKey = field.StatusKey;
    }

    public ReportFieldOption<ReportSourceKind>? SelectedSource
    {
        get => SourceOptions.FirstOrDefault(option => option.Value == Source);
        set { if (value != null && !IsTestType) Source = value.Value; }
    }

    public ReportFieldOption<ReportValueFormat>? SelectedFormat
    {
        get => FormatOptions.FirstOrDefault(option => option.Value == Format);
        set { if (value != null) Format = value.Value; }
    }

    internal ReportField ToField() => IsTestType ? _sourceField : _sourceField with
    {
        Enabled = Enabled,
        Source = Source,
        Key = Key,
        Label = Label,
        Format = Format,
        StatusKey = CanSetStatus || _sourceField.Source == Source ? StatusKey : ""
    };

    partial void OnKeyChanged(string value)
    {
        if (Source != ReportSourceKind.AllPassed && PickedKey != null && PickedKey != value)
            PickedKey = null;
        OnPropertyChanged(nameof(DisplayLabel));
    }
    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(DisplayLabel));
    partial void OnFormatChanged(ReportValueFormat value)
    {
        OnPropertyChanged(nameof(SelectedFormat));
        OnPropertyChanged(nameof(IsPoeFormat));
        OnPropertyChanged(nameof(StatusCaption));
    }
    partial void OnSourceChanged(ReportSourceKind value)
    {
        PickedKey = null;
        if (value == ReportSourceKind.AllPassed && Format is ReportValueFormat.FixedSix or ReportValueFormat.PoeMillivolts)
            Format = ReportValueFormat.BooleanNumber;
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(FormatOptions));
        OnPropertyChanged(nameof(SelectedFormat));
        OnPropertyChanged(nameof(CanSetStatus));
        OnPropertyChanged(nameof(KeyCaption));
        OnPropertyChanged(nameof(KnownKeys));
        OnPropertyChanged(nameof(HasKnownKeys));
    }

    partial void OnPickedKeyChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (Source != ReportSourceKind.AllPassed)
        {
            Key = value;
            return;
        }

        var keys = (Key ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!keys.Contains(value, StringComparer.Ordinal))
            Key = string.Join("; ", keys.Append(value));
    }
}
