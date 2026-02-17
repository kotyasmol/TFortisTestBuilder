using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBuilder.ViewModels.NodifyVM
{
    public class NodeViewModel
    {
        public string Title { get; set; } = string.Empty;
        public ObservableCollection<ConnectorViewModel> Input { get; } = new();
        public ObservableCollection<ConnectorViewModel> Output { get; } = new();
    }
}
