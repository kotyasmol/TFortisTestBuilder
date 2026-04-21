using CommunityToolkit.Mvvm.Input;
using System;

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
                Console.WriteLine($"[PendingConnection] StartCommand called, source={source?.Title}, source.Parent={source?.Parent?.Title}");
                _source = source;
            });

            FinishCommand = new RelayCommand<ConnectorViewModel>(target =>
            {
                Console.WriteLine($"[PendingConnection] FinishCommand called, target={target?.Title}, _source={_source?.Title}");

                if (_source != null && target != null && _source != target)
                {
                    if (_source.Parent != target.Parent)
                    {
                        Console.WriteLine($"[PendingConnection] Connecting {_source.Title} -> {target.Title}");
                        _editor.Connect(_source, target);
                    }
                    else
                    {
                        Console.WriteLine($"[PendingConnection] Skipped - same parent node");
                    }
                }
                else
                {
                    Console.WriteLine($"[PendingConnection] Skipped - source={_source?.Title}, target={target?.Title}");
                }

                _source = null;
            });
        }

        public void Reset() => _source = null;

        public IRelayCommand<ConnectorViewModel> StartCommand { get; }
        public IRelayCommand<ConnectorViewModel> FinishCommand { get; }
    }
}