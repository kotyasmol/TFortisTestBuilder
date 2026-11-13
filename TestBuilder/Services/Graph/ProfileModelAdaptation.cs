using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TestBuilder.Domain.Steps;
using TestBuilder.Models;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services.Graph;

/// <summary>A reversible edit of known identity fields; hardware methodology is never inferred from a name.</summary>
internal sealed class ProfileModelAdaptation
{
    readonly List<(Action Apply, Action Undo)> edits = new();
    public void Apply() { foreach (var edit in edits) edit.Apply(); }
    public void Undo() { foreach (var edit in edits.AsEnumerable().Reverse()) edit.Undo(); }
    void Edit<T>(Func<T> get, Action<T> set, T value)
    {
        var before = get();
        if (!Equals(before, value)) edits.Add((() => set(value), () => set(before)));
    }

    public static IEnumerable<NodeViewModel> All(GraphWorkspaceViewModel root)
    {
        var seen = new HashSet<GraphWorkspaceViewModel>();
        IEnumerable<NodeViewModel> Visit(GraphWorkspaceViewModel graph)
        {
            if (!seen.Add(graph)) yield break;
            foreach (var node in graph.Nodes)
            {
                yield return node;
                if (node is ICompositeNodeViewModel composite)
                    foreach (var child in Visit(composite.BodyGraph)) yield return child;
            }
        }
        foreach (var node in Visit(root)) yield return node;
        foreach (var action in root.QuickActions)
            foreach (var graph in new[] { action.Graph, action.OffGraph })
                foreach (var node in Visit(graph)) yield return node;
    }

    public static ProfileModelAdaptation Create(GraphWorkspaceViewModel root, EquipmentModel? source, EquipmentModel target)
    {
        var plan = new ProfileModelAdaptation();
        var nodes = All(root).ToArray();
        var serialNames = nodes.OfType<GetSerialNumberFromServerNodeViewModel>().Select(n => n.OutputVariableName)
            .Append("SerialNumber").Append("NetTest.SerialNumber").ToHashSet(StringComparer.Ordinal);
        var series = target.SerialSeries;
        foreach (var node in nodes)
        {
            var reasons = new List<string>();
            switch (node)
            {
                case GetSerialNumberFromServerNodeViewModel serial:
                    plan.Edit(() => serial.DeviceType, v => serial.DeviceType = v, target.EffectiveServerDeviceType ?? "");
                    plan.Edit(() => serial.ExpectedSerialPrefix, v => serial.ExpectedSerialPrefix = v, "");
                    plan.Edit(() => serial.UseFixedSerialNumber, v => serial.UseFixedSerialNumber = v, false);
                    plan.Edit(() => serial.FixedSerialNumber, v => serial.FixedSerialNumber = v, 0);
                    if (string.IsNullOrWhiteSpace(target.EffectiveServerDeviceType)) reasons.Add("Заполните тип на сервере в справочнике изделия.");
                    if (target.RtlReport?.AssignSerialOnReport == true) reasons.Add("Эта модель получает номер из ответа на отчёт. Проверьте необходимость отдельного запроса номера.");
                    if (serial.ServerBaseUrl.Length > 0) reasons.Add("В ноде задан отдельный адрес сервера. Проверьте его и переменную CPU ID.");
                    break;
                case BuildMacFromSerialNodeViewModel mac:
                    plan.Edit(() => mac.SerialOffset, v => mac.SerialOffset = v, series?.SerialOffset ?? 0);
                    plan.Edit(() => mac.MacPrefix, v => mac.MacPrefix = v, series?.MacPrefix ?? "");
                    if (series?.MacPrefix == null) reasons.Add("В справочнике не задана серия с префиксом MAC. Проверьте, нужен ли расчёт MAC этой модели.");
                    break;
                case CheckVariableRangeNodeViewModel range when serialNames.Contains(range.VariableName) && series != null:
                    plan.Edit(() => range.Min, v => range.Min = v, (double)series.FirstSerialNumber);
                    plan.Edit(() => range.Max, v => range.Max = v, (double)series.LastSerialNumber);
                    plan.Edit(() => range.Inclusive, v => range.Inclusive = v, true);
                    plan.Edit(() => range.FailMessage, v => range.FailMessage = v,
                        $"Серийный номер для «{target.Name}» должен быть {series.FirstSerialNumber}–{series.LastSerialNumber}.");
                    break;
                case BuildTestReportNodeViewModel report:
                    plan.Edit(() => report.ExpectedSerialPrefix, v => report.ExpectedSerialPrefix = v, "");
                    reasons.Add("Проверьте состав отчёта и источники результатов для новой модели. Номер и формат данных берутся из её справочника.");
                    break;
                case SendTestReportNodeViewModel send:
                    if (string.IsNullOrWhiteSpace(send.Endpoint) || IsLegacyEndpoint(send.Endpoint) || RtlTestReport.IsEndpoint(send.Endpoint))
                        plan.Edit(() => send.Endpoint, v => send.Endpoint = v,
                            target.RtlReport == null ? "/api/Api.svc/result.json" : RtlTestReport.Endpoint);
                    else reasons.Add("Указан нестандартный адрес метода отчёта. Проверьте его совместимость с моделью.");
                    if (send.ServerBaseUrl.Length > 0) reasons.Add("В ноде задан отдельный адрес сервера. Проверьте адрес и настройки доставки.");
                    break;
                case PrintLabelNodeViewModel print:
                    plan.RetargetLabel(print, source, target, 0, reasons);
                    break;
                case SetProMacNodeViewModel pro:
                    plan.Edit(() => pro.BoardVersion, v => pro.BoardVersion = v, series?.BoardVersion ?? "");
                    reasons.Add("Проверьте способ записи MAC, скрипт и адрес устройства. Имя платы берётся из справочника.");
                    if (string.IsNullOrWhiteSpace(series?.BoardVersion)) reasons.Add("В карточке не задано имя платы для записи Pro MAC. Возможно, этой модели нужна другая нода записи.");
                    break;
                case SelfTestCheckNodeViewModel selftest:
                    reasons.Add("Проверьте адрес и формат selftest, поля устройства и допуски. Они не определяются по названию модели.");
                    if (selftest.Psw2G6FHardwareChecks)
                    {
                        plan.Edit(() => selftest.Psw2G6FHardwareChecks, v => selftest.Psw2G6FHardwareChecks = v, false);
                        reasons.Add("Скрытые проверки старого PSW-2G6F+ отключены для нового изделия. Задайте нужные аппаратные проверки явно.");
                    }
                    break;
                case RunDataTestNodeViewModel:
                    reasons.Add("Проверьте количество и назначение портов, IP-пары, скорости и допуски передачи данных."); break;
                case ModbusWriteNodeViewModel or CheckRegisterRangeNodeViewModel or WaitUntilNodeViewModel or ForEachSlaveNodeViewModel:
                    reasons.Add("Проверьте оборудование стенда, адреса устройств/регистров, диапазон обхода, значения и допуски для нового изделия."); break;
                case SetPswMacNodeViewModel or UpdatePswFirmwareNodeViewModel:
                    reasons.Add("Проверьте семейство устройства, адрес, способ записи и файл/версию прошивки. Эти параметры не заменяются по названию модели."); break;
                case ReadHttpVariableNodeViewModel or WaitVariableUntilNodeViewModel or SerialConsoleNodeViewModel or RunProgramNodeViewModel or ParseResponseNodeViewModel:
                    reasons.Add("Проверьте адрес/команду, формат ответа, поля, переменные и ожидания новой модели."); break;
                case CheckVariableEqualityNodeViewModel or CheckVariableRangeNodeViewModel or CompareVariablesNodeViewModel or CheckLedColorNodeViewModel:
                    reasons.Add("Проверьте источники значений, ожидаемый тип устройства, версии и допуски новой модели."); break;
                case DelayNodeViewModel:
                    reasons.Add("Проверьте длительность ожидания для нового изделия."); break;
                case OperatorActionNodeViewModel:
                    reasons.Add("Проверьте инструкцию и запрашиваемые у оператора данные для нового изделия."); break;
            }
            if (source != null && node is LabelNodeViewModel label && label.Text.Contains(source.Name, StringComparison.OrdinalIgnoreCase))
                reasons.Add("Подпись содержит название исходного изделия. Проверьте описание и указанные параметры.");
            if (source != null && node is SubtestNodeViewModel sub &&
                (sub.Name.Contains(source.Name, StringComparison.OrdinalIgnoreCase) || sub.Description.Contains(source.Name, StringComparison.OrdinalIgnoreCase)))
                reasons.Add("Название или описание подтеста содержит исходное изделие. Проверьте описание этапа.");
            // A second model change supersedes an earlier review, including for automatically bound nodes.
            NodeModelReview? review = reasons.Count == 0 ? null : new(target.Id, target.Name, string.Join("\n", reasons));
            plan.Edit(() => node.ModelReview, v => node.ModelReview = v, review);
        }
        return plan;
    }

    static bool IsLegacyEndpoint(string endpoint) => endpoint.Trim().TrimStart('/').Equals("api/Api.svc/result.json", StringComparison.OrdinalIgnoreCase);

    void RetargetLabel(PrintLabelNodeViewModel print, EquipmentModel? source, EquipmentModel target, int variant, List<string> reasons)
    {
        var oldTemplate = print.LabelTemplate;
        var oldFormat = print.UseQtProZplFormat;
        var oldFlags = print.DataSourceFlags;
        var oldSerial = print.ManualSerialNumber;
        var oldMac = print.ManualMacAddress;
        var templates = AppSettings.Instance.LabelTemplates.Concat(LabelTemplateCatalog.Defaults()).ToArray();
        var board = target.RtlReport != null;
        var template = oldTemplate?.Copy();
        if (template != null && template.UseShortSerial == board)
            template = templates.First(t => board ? !t.UseShortSerial && t.Language == "EPL" : t.UseShortSerial && t.RequiresMac && t.Language == "ZPL").Copy();
        if (template != null)
        {
            var names = new[] { source?.Name, oldTemplate?.Name }.OfType<string>().Where(n => n.Length > 0 && !n.Equals(target.Name, StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
            template.Command = ParameterizeNames(template.Command, names);
            template.Name = target.Name;
            if (target.SerialSeries != null && target.LabelCode.HasValue)
                template = LabelPrintContent.ForModel(template, target, variant);
        }
        if (!board && (target.SerialSeries == null || target.LabelCode == null)) reasons.Add("Для этикетки заполните код модели и серию в справочнике.");
        var updated = template;
        edits.Add((() =>
        {
            print.LabelTemplate = updated; print.UseQtProZplFormat = updated?.UseShortSerial ?? !board;
            print.DataSourceFlags = (false, false); print.ManualSerialNumber = ""; print.ManualMacAddress = "";
        }, () =>
        {
            print.LabelTemplate = oldTemplate; print.UseQtProZplFormat = oldFormat;
            print.DataSourceFlags = oldFlags; print.ManualSerialNumber = oldSerial; print.ManualMacAddress = oldMac;
        }));
        reasons.Add("Модель, код и серия этикетки обновлены. Проверьте макет, размер, комплектацию, число копий и переменные текущего устройства.");
        if (oldFlags != (false, false) || oldSerial.Length > 0 || oldMac.Length > 0)
            reasons.Add("Ручные данные и режим повтора последней этикетки сброшены: они относились к прежнему изделию.");
    }

    internal static string ParameterizeNames(string command, IEnumerable<string> names)
    {
        string Replace(string text)
        {
            foreach (var name in names.OrderByDescending(n => n.Length)) text = text.Replace(name, "{{Name}}", StringComparison.OrdinalIgnoreCase);
            return text;
        }
        // Replace only printable text, never paths, JSON selectors, IDs, scripts or graph labels.
        command = Regex.Replace(command, @"(\^FD)([^\^]*)(\^FS)", m => m.Groups[1].Value + Replace(m.Groups[2].Value) + m.Groups[3].Value);
        return Regex.Replace(command, "(?m)^(A[^\\r\\n]*,\")([^\"]*)(\")$", m => m.Groups[1].Value + Replace(m.Groups[2].Value) + m.Groups[3].Value);
    }
}
