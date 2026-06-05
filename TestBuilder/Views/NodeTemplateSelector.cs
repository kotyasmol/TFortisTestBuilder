using Avalonia.Controls;
using Avalonia.Controls.Templates;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Views
{
    public class NodeTemplateSelector : IDataTemplate
    {
        public IDataTemplate? DefaultTemplate { get; set; }

        public IDataTemplate? ModbusWriteTemplate { get; set; }

        public IDataTemplate? CheckRegisterRangeTemplate { get; set; }

        public IDataTemplate? DelayTemplate { get; set; }

        public IDataTemplate? LabelTemplate { get; set; }

        public IDataTemplate? ForEachSlaveTemplate { get; set; }

        public IDataTemplate? HttpRequestTemplate { get; set; }

        public IDataTemplate? RequestTestPageTemplate { get; set; }

        public IDataTemplate? ParseTestPageTemplate { get; set; }

        public IDataTemplate? CheckVariableEqualityTemplate { get; set; }

        public IDataTemplate? CheckVariableRangeTemplate { get; set; }

        public IDataTemplate? ClearArpCacheTemplate { get; set; }

        public IDataTemplate? GetSerialNumberTemplate { get; set; }

        public IDataTemplate? SendUdpSetMacTemplate { get; set; }

        public IDataTemplate? RunDataTestTemplate { get; set; }

        public IDataTemplate? GetUpsStatusTemplate { get; set; }

        public IDataTemplate? GetUpsVoltageTemplate { get; set; }

        public IDataTemplate? PrintLabelTemplate { get; set; }

        public IDataTemplate? SendTestReportTemplate { get; set; }

        public IDataTemplate? CheckRegisterEqualityTemplate { get; set; }

        public IDataTemplate? WaitUntilTemplate { get; set; }

        public IDataTemplate? PollRegisterTemplate { get; set; }

        public IDataTemplate? OperatorActionTemplate { get; set; }

        public Control? Build(object? param)
        {
            return param switch
            {
                ModbusWriteNodeViewModel => ModbusWriteTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                CheckRegisterRangeNodeViewModel => CheckRegisterRangeTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                CheckRegisterEqualityNodeViewModel => CheckRegisterEqualityTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                WaitUntilNodeViewModel => WaitUntilTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                PollRegisterNodeViewModel => PollRegisterTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                DelayNodeViewModel => DelayTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                LabelNodeViewModel => LabelTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                ForEachSlaveNodeViewModel => ForEachSlaveTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                HttpRequestNodeViewModel => HttpRequestTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                RequestTestPageNodeViewModel => RequestTestPageTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                ParseTestPageNodeViewModel => ParseTestPageTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                CheckVariableEqualityNodeViewModel => CheckVariableEqualityTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                CheckVariableRangeNodeViewModel => CheckVariableRangeTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                ClearArpCacheNodeViewModel => ClearArpCacheTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                GetSerialNumberFromServerNodeViewModel => GetSerialNumberTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                SendUdpSetMacPacketNodeViewModel => SendUdpSetMacTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                RunDataTestNodeViewModel => RunDataTestTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                GetUpsStatusNodeViewModel => GetUpsStatusTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                GetUpsVoltageNodeViewModel => GetUpsVoltageTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                PrintLabelNodeViewModel => PrintLabelTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                SendTestReportNodeViewModel => SendTestReportTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                OperatorActionNodeViewModel => OperatorActionTemplate?.Build(param) ?? DefaultTemplate?.Build(param),
                _ => DefaultTemplate?.Build(param)
            };
        }

        public bool Match(object? data) => true;
    }
}
