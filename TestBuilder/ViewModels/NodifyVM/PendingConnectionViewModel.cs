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
                if (_source != null && target != null)
                {
                    _editor.Connect(_source, target);
                }
            });
        }

        public IRelayCommand<ConnectorViewModel> StartCommand { get; }
        public IRelayCommand<ConnectorViewModel> FinishCommand { get; }
    }
}