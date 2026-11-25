using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Services;

namespace TestBuilder.ViewModels;

public sealed record LabelSourceOption(string Id, string Title)
{
    public override string ToString() => Title;
    public static LabelSourceOption For(string id) => new(id, id switch {
        "Model" => "Изделие и исполнение профиля", "Step" => "Из настроек шага печати",
        "Variable" => "Переменная проверки", "Constant" => "Постоянное значение",
        "Automatic" => "Вычислить из полного SN", _ => id });
}

public partial class LabelFieldSourceViewModel : ObservableObject
{
    public string Field { get; }
    public string Title => LabelDataBindings.Title(Field);
    public IReadOnlyList<LabelSourceOption> Options { get; }
    [ObservableProperty] private LabelSourceOption selected;
    [ObservableProperty] private string value;
    public bool HasValue => Selected.Id is "Variable" or "Constant";
    public string ValueTitle => Selected.Id == "Variable" ? "Имя переменной" : "Значение";
    public string Hint => Selected.Id switch {
        "Variable" => "Без {{скобок}}. Переменную должен заполнить шаг до печати.",
        "Constant" => "Одинаково для всех изделий с этим типом этикетки.",
        "Automatic" => "Полный SN минус начало серии изделия.",
        "Model" => Field == "Cabinet" ? "Шкаф выбранного исполнения из справочника изделий." : "Данные изделия, выбранного в профиле.",
        _ => Field == "Serial" ? "Обычно SerialNumber. Ручной ввод в шаге имеет приоритет."
            : Field == "Mac" ? "Обычно Dut.default_mac; имя задано в шаге печати."
            : "По серии изделия; без серии — переменная короткого SN из шага, обычно SerialShort."
    };
    public event Action? Changed;
    public LabelFieldSourceViewModel(string field, LabelFieldBinding binding)
    {
        Field = field; Options = LabelDataBindings.Sources(field).Select(LabelSourceOption.For).ToArray();
        selected = Options.Single(o => o.Id == binding.Source); value = binding.Value;
    }
    partial void OnSelectedChanged(LabelSourceOption value)
    {
        OnPropertyChanged(nameof(HasValue)); OnPropertyChanged(nameof(ValueTitle)); OnPropertyChanged(nameof(Hint)); Changed?.Invoke();
    }
    partial void OnValueChanged(string value) => Changed?.Invoke();
    public LabelFieldBinding Binding => new(Selected.Id, Value.Trim());
}

public sealed record LabelTypeChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

public partial class LabelProductBindingRow : ObservableObject
{
    public string ModelId { get; }
    public string Name { get; }
    public string SavedTypeId { get; set; }
    public IReadOnlyList<LabelTypeChoice> Choices { get; private set; }
    [ObservableProperty] private LabelTypeChoice selected;
    public bool IsDirty => !string.Equals(Selected.Id, SavedTypeId, StringComparison.OrdinalIgnoreCase);
    public event Action? Changed;
    public LabelProductBindingRow(string modelId, string name, string typeId, IReadOnlyList<LabelTypeChoice> choices)
    {
        ModelId = modelId; Name = name; SavedTypeId = typeId; Choices = choices;
        selected = choices.Single(c => string.Equals(c.Id, typeId, StringComparison.OrdinalIgnoreCase));
    }
    public void UpdateChoices(IReadOnlyList<LabelTypeChoice> choices)
    {
        var id = Selected.Id; Choices = choices; OnPropertyChanged(nameof(Choices));
        Selected = choices.SingleOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
    }
    partial void OnSelectedChanged(LabelTypeChoice value) { OnPropertyChanged(nameof(IsDirty)); Changed?.Invoke(); }
}
