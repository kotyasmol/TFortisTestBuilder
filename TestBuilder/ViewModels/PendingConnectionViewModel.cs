using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBuilder.ViewModels
{
    public class PendingConnectionViewModel
    {
        private readonly MainWindowViewModel _editor;
        private ConnectorViewModel? _source;

        public PendingConnectionViewModel(MainWindowViewModel editor)
        {
            _editor = editor;

            StartCommand = new RelayCommand<ConnectorViewModel>(source =>
            {
                _source = source;
            });

            FinishCommand = new RelayCommand<ConnectorViewModel>(target =>
            {
                if (_source != null && target != null)
                    _editor.Connect(_source, target);
            });
        }

        public IRelayCommand<ConnectorViewModel> StartCommand { get; }
        public IRelayCommand<ConnectorViewModel> FinishCommand { get; }
    }

}
