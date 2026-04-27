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
            Title = "Body Start";

            Out = new ConnectorViewModel { Title = "Out" };
            AddOutput(Out);
        }
    }
}
