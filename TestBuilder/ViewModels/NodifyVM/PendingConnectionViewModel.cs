using CommunityToolkit.Mvvm.Input;

namespace TestBuilder.ViewModels.NodifyVM
{
    public class PendingConnectionViewModel
    {
        private readonly IGraphEditor _editor;
        private ConnectorViewModel? _source;

        public PendingConnectionViewModel(IGraphEditor editor)
        {
            _editor = editor;

            StartCommand = new RelayCommand<ConnectorViewModel>(source =>
            {
                _source = source;
            });

            FinishCommand = new RelayCommand<ConnectorViewModel>(target =>
            {
                if (_source != null && target != null && _source != target)
                {
                    // Не соединяем коннекторы одной и той же ноды
                    if (_source.Parent != target.Parent)
                        _editor.Connect(_source, target);
                }

                _source = null; // Всегда сбрасываем после попытки соединения
            });
        }

        // Сброс незавершённого соединения (например после ClearGraph)
        public void Reset() => _source = null;

        public IRelayCommand<ConnectorViewModel> StartCommand { get; }
        public IRelayCommand<ConnectorViewModel> FinishCommand { get; }
    }
}