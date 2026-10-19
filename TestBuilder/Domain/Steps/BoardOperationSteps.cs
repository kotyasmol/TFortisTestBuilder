using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps;

public sealed class SerialConsoleStep(SerialConsoleOptions options, ISerialConsoleSession console, ILogger logger) : ITestStep
{
    public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token)
    {
        context.Variables.Remove(options.OutputVariableName);
        try
        {
            token.ThrowIfCancellationRequested(); await context.WaitWhilePausedAsync(token);
            if (string.IsNullOrWhiteSpace(options.OutputVariableName)) throw new InvalidOperationException("Укажите переменную ответа консоли.");
            var port = string.IsNullOrWhiteSpace(options.PortName) ? AppSettings.Instance.ConsolePort : ResponseParser.Expand(options.PortName, context);
            var response = await console.ExchangeAsync(port, options.BaudRate, ResponseParser.Expand(options.Command, context), options.ResponseTerminator, options.QuietMs, options.TimeoutMs, token);
            if (string.IsNullOrEmpty(response)) throw new InvalidOperationException("Пустой ответ консоли.");
            context.SetVariable(options.OutputVariableName, response);
            if (context.RequireFreshMonitoring)
                foreach (var slave in context.ModbusRegisters.SlaveIds) context.RequiredPollAfter[slave] = System.Diagnostics.Stopwatch.GetTimestamp();
            logger.Info($"[OK] Консоль: получено {response.Length} символов → {options.OutputVariableName}.");
            return StepResult.True;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.Warning("[ОШИБКА] Консоль: " + ex.Message); return StepResult.False; }
    }
}
public sealed class RunProgramStep(RunProgramOptions options, IExternalProgramRunner runner, ILogger logger) : ITestStep
{
    public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token)
    {
        foreach (var suffix in new[] { ".Output", ".Error", ".ExitCode" }) context.Variables.Remove(options.OutputPrefix + suffix);
        try
        {
            token.ThrowIfCancellationRequested(); await context.WaitWhilePausedAsync(token);
            if (string.IsNullOrWhiteSpace(options.OutputPrefix)) throw new InvalidOperationException("Укажите префикс переменных программы.");
            var args = options.Arguments.Replace("\r\n", "\n").Split('\n').Where(x => x.Length > 0).Select(x => ResponseParser.Expand(x, context)).ToArray();
            var result = await runner.RunAsync(ResponseParser.Expand(options.ProgramPath, context), args, ResponseParser.Expand(options.WorkingDirectory, context), options.TimeoutMs, token);
            context.SetVariable(options.OutputPrefix + ".Output", result.Output);
            context.SetVariable(options.OutputPrefix + ".Error", result.Error);
            context.SetVariable(options.OutputPrefix + ".ExitCode", result.ExitCode);
            var success = result.ExitCode == options.ExpectedExitCode;
            logger.Info($"[{(success ? "OK" : "ОШИБКА")}] Программа: exit code {result.ExitCode}; ожидался {options.ExpectedExitCode}.");
            return success ? StepResult.True : StepResult.False;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.Warning("[ОШИБКА] Программа: " + ex.Message); return StepResult.False; }
    }
}
public sealed class ParseResponseStep(ParseResponseOptions options, ILogger logger) : ITestStep
{
    public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Read before clearing so a deliberate in-place conversion is supported.
        var exists = context.Variables.TryGetValue(options.SourceVariableName, out var source);
        context.Variables.Remove(options.OutputVariableName);
        try
        {
            if (!exists) throw new InvalidOperationException("Нет исходной переменной: " + options.SourceVariableName);
            if (string.IsNullOrWhiteSpace(options.OutputVariableName)) throw new InvalidOperationException("Укажите переменную результата.");
            var parsed = ResponseParser.Parse(Convert.ToString(source, CultureInfo.InvariantCulture) ?? "", options.ParseMode, options.Selector, options.ValueType);
            context.SetVariable(options.OutputVariableName, parsed);
            logger.Info($"[OK] Ответ разобран → {options.OutputVariableName}.");
            return Task.FromResult(StepResult.True);
        }
        catch (Exception ex) { logger.Warning("[ОШИБКА] Разбор ответа: " + ex.Message); return Task.FromResult(StepResult.False); }
    }
}
public sealed class CheckLedColorStep(CheckLedColorOptions options, ILogger logger) : ITestStep
{
    public static bool Matches(int r, int g, int b, int w, string color, int margin, int minimum) => color.ToLowerInvariant() switch
    {
        "red" => r > g + margin && r > b + margin && r > minimum,
        "green" => g > r + margin && g > b + margin && g > minimum,
        "blue" => b > r + margin && b > g + margin && b > minimum,
        "white" => Math.Abs(r - g) < margin && Math.Abs(r - b) < margin && w > minimum,
        _ => false
    };
    public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken token)
    {
        context.Variables.Remove(options.OutputPrefix + ".Passed");
        context.Variables.Remove(options.OutputPrefix + ".Values");
        token.ThrowIfCancellationRequested();
        var slave = options.UseCurrentSlaveId ? context.CurrentSlaveId : options.SlaveId;
        if (slave is null or < 1 or > 247 || options.ColorMargin is < 0 or > 65535 || options.MinIntensity is < 0 or > 65535 ||
            !new[] { "red", "green", "blue", "white" }.Contains(options.ExpectedColor.ToLowerInvariant()))
        { logger.Warning("[ОШИБКА] LED: неверный slave, цвет или порог."); return StepResult.False; }
        var values = new int[4]; var addresses = new[] { options.RedAddress, options.GreenAddress, options.BlueAddress, options.WhiteAddress };
        for (var i = 0; i < 4; i++)
        {
            var result = await ModbusRegisterReadHelper.ReadAsync(context, null, slave.Value, addresses[i], false, token);
            if (!result.Success) { logger.Warning("[ОШИБКА] LED: " + result.Error); return StepResult.False; }
            values[i] = result.Value;
        }
        var ok = Matches(values[0], values[1], values[2], values[3], options.ExpectedColor, options.ColorMargin, options.MinIntensity);
        var details = $"R:{values[0]}, G:{values[1]}, B:{values[2]}, W:{values[3]}, ожидается {options.ExpectedColor}";
        context.SetVariable(options.OutputPrefix + ".Passed", ok);
        context.SetVariable(options.OutputPrefix + ".Values", details);
        context.AddReportEntry("Цвет " + options.OutputPrefix, ok, details);
        logger.Info($"[{(ok ? "OK" : "ОШИБКА")}] LED: {details}");
        return ok ? StepResult.True : StepResult.False;
    }
}
