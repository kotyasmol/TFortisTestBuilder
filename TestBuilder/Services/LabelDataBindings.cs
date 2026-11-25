using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Services;

public sealed record LabelFieldBinding(string Source, string Value = "");

/// <summary>Sources are resolved once for a new test label. History keeps the resolved values.</summary>
public sealed record LabelDataBindings
{
    public LabelFieldBinding Name { get; init; } = new("Model");
    public LabelFieldBinding Serial { get; init; } = new("Step");
    public LabelFieldBinding ShortSerial { get; init; } = new("Step");
    public LabelFieldBinding Mac { get; init; } = new("Step");
    public LabelFieldBinding DeviceCode { get; init; } = new("Model");
    public LabelFieldBinding Cabinet { get; init; } = new("Model");

    public static readonly string[] Fields = { "Name", "Serial", "ShortSerial", "Mac", "DeviceCode", "Cabinet" };
    public LabelFieldBinding Get(string field) => field switch
    {
        "Name" => Name, "Serial" => Serial, "ShortSerial" => ShortSerial,
        "Mac" => Mac, "DeviceCode" => DeviceCode, "Cabinet" => Cabinet,
        _ => throw new ArgumentException("Неизвестное поле этикетки.")
    };
    public LabelDataBindings With(string field, LabelFieldBinding value) => field switch
    {
        "Name" => this with { Name = value }, "Serial" => this with { Serial = value },
        "ShortSerial" => this with { ShortSerial = value }, "Mac" => this with { Mac = value },
        "DeviceCode" => this with { DeviceCode = value }, "Cabinet" => this with { Cabinet = value },
        _ => throw new ArgumentException("Неизвестное поле этикетки.")
    };
    public static string Title(string field) => field switch
    {
        "Name" => "Название устройства", "Serial" => "Полный серийный номер", "ShortSerial" => "Короткий серийный номер",
        "Mac" => "MAC", "DeviceCode" => "Код изделия в штрихкоде", "Cabinet" => "Шкаф", _ => field
    };
    public static string DefaultSource(string field) => field is "Serial" or "ShortSerial" or "Mac" ? "Step" : "Model";
    public static IEnumerable<string> Sources(string field)
        => field is "Serial" or "Mac" ? new[] { "Step", "Variable" }
        : field == "ShortSerial" ? new[] { "Step", "Automatic", "Variable" }
        : new[] { "Model", "Variable", "Constant" };

    public void Validate()
    {
        foreach (var field in Fields)
        {
            var binding = Get(field);
            if (binding == null || !Sources(field).Contains(binding.Source) || binding.Value == null || binding.Value.Length > 256)
                throw new InvalidOperationException($"{Title(field)}: неизвестный источник данных.");
            if (binding.Source == "Variable" && (string.IsNullOrWhiteSpace(binding.Value) || binding.Value.Any(char.IsWhiteSpace) || binding.Value.IndexOfAny(new[] { '{', '}' }) >= 0 || !LabelTemplateDefinition.IsSafeLabelText(binding.Value)))
                throw new InvalidOperationException($"{Title(field)}: укажите имя переменной проверки без скобок и пробелов.");
            if (binding.Source == "Constant" && (string.IsNullOrWhiteSpace(binding.Value) || !LabelTemplateDefinition.IsSafeLabelText(binding.Value)))
                throw new InvalidOperationException($"{Title(field)}: заполните постоянное значение без команд принтера.");
            if (field == "DeviceCode" && binding.Source == "Constant") ParseCode(binding.Value);
        }
    }

    internal static string Read(TestContext context, string field, LabelFieldBinding binding)
    {
        if (binding.Source == "Constant") return binding.Value;
        if (!context.Variables.TryGetValue(binding.Value, out var raw) || string.IsNullOrWhiteSpace(Convert.ToString(raw, CultureInfo.InvariantCulture)))
            throw new InvalidOperationException($"{Title(field)}: переменная '{binding.Value}' не найдена или пуста. Её должен заполнить шаг до печати.");
        return Convert.ToString(raw, CultureInfo.InvariantCulture)!.Trim();
    }
    internal static int ParseCode(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var code))
            throw new InvalidOperationException("Код изделия в штрихкоде должен быть целым неотрицательным числом.");
        return code;
    }
    internal void ApplyPresentation(LabelTemplateDefinition template, TestContext? context = null)
    {
        foreach (var field in new[] { "Name", "DeviceCode", "Cabinet" })
        {
            var binding = Get(field);
            if (binding.Source == "Model" || binding.Source == "Variable" && context == null) continue;
            var value = context == null ? binding.Value : Read(context, field, binding);
            switch (field)
            {
                case "Name": template.Name = value; break;
                case "Cabinet": template.CabinetName = value; break;
                case "DeviceCode": template.DeviceCode = ParseCode(value); template.BarcodePrefix = template.DeviceCode.ToString("D3", CultureInfo.InvariantCulture); break;
            }
        }
        template.Validate();
    }
}
