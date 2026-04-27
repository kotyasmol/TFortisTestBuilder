using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    /// <summary>
    /// Техническая конечная нода тела составного блока.
    /// Когда исполнитель доходит до нее, текущая итерация вложенного графа считается завершенной.
    /// </summary>
    public sealed class BodyEndNodeViewModel : NodeViewModel
    {
        public ConnectorViewModel In { get; }

        public BodyEndNodeViewModel()
        {
            Title = "Body End";

            In = new ConnectorViewModel { Title = "In" };
            AddInput(In);
        }
    }
}
