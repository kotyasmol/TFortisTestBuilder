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

        public Control? Build(object? param)
        {
            return param switch
            {
                ModbusWriteNodeViewModel => ModbusWriteTemplate?.Build(param),
                CheckRegisterRangeNodeViewModel => CheckRegisterRangeTemplate?.Build(param),
                DelayNodeViewModel => DelayTemplate?.Build(param),
                LabelNodeViewModel => LabelTemplate?.Build(param),
                ForEachSlaveNodeViewModel => ForEachSlaveTemplate?.Build(param),
                HttpRequestNodeViewModel => HttpRequestTemplate?.Build(param),
                _ => DefaultTemplate?.Build(param)
            };
        }

        public bool Match(object? data) => true;
    }
}
