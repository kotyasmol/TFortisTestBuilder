using Avalonia.Controls;
using Avalonia.Controls.Templates;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Views;

public class NodeTemplateSelector : IDataTemplate
{
    public IDataTemplate? DefaultTemplate { get; set; }
    public IDataTemplate? ModbusWriteTemplate { get; set; }

    public Control? Build(object? param)
    {
        if (param is ModbusWriteNodeViewModel)
            return ModbusWriteTemplate?.Build(param);
        return DefaultTemplate?.Build(param);
    }

    public bool Match(object? data) => true;
}