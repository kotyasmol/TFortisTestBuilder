using Avalonia.Controls;
using Avalonia.Controls.Templates;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Views;

public class NodeTemplateSelector : IDataTemplate
{
    public IDataTemplate? DefaultTemplate { get; set; }
    public IDataTemplate? ModbusWriteTemplate { get; set; }
    public IDataTemplate? CheckRegisterRangeTemplate { get; set; }

    public Control? Build(object? param)
    {
        return param switch
        {
            ModbusWriteNodeViewModel =>
                ModbusWriteTemplate?.Build(param),

            CheckRegisterRangeNodeViewModel =>
                CheckRegisterRangeTemplate?.Build(param),

            _ =>
                DefaultTemplate?.Build(param)
        };
    }

    public bool Match(object? data) => true;
}