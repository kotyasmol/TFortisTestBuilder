using System;
using TestBuilder.Domain.Steps;

namespace TestBuilder.Domain.Execution;

public enum TestErrorCode
{
    Unexpected = 1000,
    ProfileLoad = 1001,
    GraphInvalid = 1002,
    MissingTransition = 1003,
    ParallelGraph = 1004,
    ParallelVariables = 1005,
    Preparation = 1006,
    Cleanup = 1007,
    Startup = 1008,
    ProfileAction = 1009,
    RunLog = 1010,
    Subtest = 1011,
    StandConnection = 1101,
    StandEquipment = 1102,
    StandDisconnected = 1103,
    ModbusRead = 1104,
    ModbusWrite = 1105,
    ModbusReadBack = 1106,
    RegisterRange = 1107,
    RegisterWait = 1108,
    VariableMissing = 1201,
    VariableFormat = 1202,
    VariableCheck = 1203,
    VariableWait = 1204,
    OperatorAction = 1205,
    Loop = 1206,
    Arp = 1301,
    HttpRead = 1302,
    SelftestConnection = 1303,
    SelftestResponse = 1304,
    SelftestCheck = 1305,
    SerialConfig = 1401,
    SerialRequest = 1402,
    SerialRange = 1403,
    MacBuild = 1404,
    ProMacWrite = 1405,
    PswMacWrite = 1406,
    Firmware = 1407,
    DataTestConfig = 1501,
    DataTestPair = 1502,
    DataTestStand = 1503,
    DataTestLoss = 1504,
    LabelData = 1601,
    Printer = 1602,
    ReportBuild = 1603,
    ReportSend = 1604,
    ReportStorage = 1605,
    BoardConsole = 1701,
    Program = 1702,
    ResponseParse = 1703,
    Led = 1704
}

internal static class TestErrorCodes
{
    public static string Format(this TestErrorCode code) => $"TB{(int)code:D4}";

    public static T WithCode<T>(this T exception, TestErrorCode code) where T : Exception
    {
        exception.Data[typeof(TestErrorCode)] = code;
        return exception;
    }

    public static TestErrorCode Resolve(Exception exception, TestErrorCode fallback) =>
        exception.Data[typeof(TestErrorCode)] is TestErrorCode code ? code :
        exception.InnerException is { } inner ? Resolve(inner, fallback) : fallback;

    public static TestErrorCode ForStep(ITestStep? step) => step switch
    {
        ModbusWriteStep => TestErrorCode.ModbusWrite,
        CheckRegisterRangeStep => TestErrorCode.RegisterRange,
        WaitUntilStep => TestErrorCode.RegisterWait,
        CheckVariableEqualityStep or CheckVariableRangeStep or CompareVariablesStep => TestErrorCode.VariableCheck,
        WaitVariableUntilStep => TestErrorCode.VariableWait,
        OperatorActionStep => TestErrorCode.OperatorAction,
        ForEachSlaveStep => TestErrorCode.Loop,
        ContinueOnErrorStep wrapper => ForStep(wrapper.Inner),
        SubtestStep => TestErrorCode.Subtest,
        ClearArpCacheStep => TestErrorCode.Arp,
        ReadHttpVariableStep => TestErrorCode.HttpRead,
        SelfTestCheckStep => TestErrorCode.SelftestCheck,
        GetSerialNumberFromServerStep => TestErrorCode.SerialRequest,
        BuildMacFromSerialStep => TestErrorCode.MacBuild,
        SetProMacStep => TestErrorCode.ProMacWrite,
        SetPswMacStep => TestErrorCode.PswMacWrite,
        UpdatePswFirmwareStep => TestErrorCode.Firmware,
        RunDataTestStep => TestErrorCode.DataTestConfig,
        PrintLabelStep => TestErrorCode.Printer,
        BuildTestReportStep => TestErrorCode.ReportBuild,
        SendTestReportStep => TestErrorCode.ReportSend,
        SerialConsoleStep => TestErrorCode.BoardConsole,
        RunProgramStep => TestErrorCode.Program,
        ParseResponseStep => TestErrorCode.ResponseParse,
        CheckLedColorStep => TestErrorCode.Led,
        _ => TestErrorCode.Unexpected
    };
}
