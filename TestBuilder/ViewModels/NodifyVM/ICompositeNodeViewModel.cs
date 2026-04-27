using TestBuilder.ViewModels.Graphs;

namespace TestBuilder.ViewModels.NodifyVM
{
    /// <summary>
    /// Нода, внутри которой хранится отдельный вложенный граф.
    /// Используется для Scratch-подобных конструкций: циклов, групп, условий и т.д.
    /// </summary>
    public interface ICompositeNodeViewModel
    {
        GraphWorkspaceViewModel BodyGraph { get; }
    }
}
