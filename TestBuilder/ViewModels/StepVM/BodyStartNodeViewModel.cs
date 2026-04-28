using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    /// <summary>
    /// Техническая стартовая нода тела составного блока.
    /// Вручную добавлять ее не нужно: она создается автоматически внутри For Slaves.
    /// </summary>
    public sealed class BodyStartNodeViewModel : NodeViewModel
    {
        public ConnectorViewModel Out { get; }

        public BodyStartNodeViewModel()
        {
            Title = "Тело: начало";

            Out = new ConnectorViewModel { Title = "Выход" };
            AddOutput(Out);
        }
    }
}