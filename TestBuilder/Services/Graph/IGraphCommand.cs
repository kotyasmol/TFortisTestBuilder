namespace TestBuilder.Services.Graph
{
    public interface IGraphCommand
    {
        void Execute();
        void Undo();
    }
}
