using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBuilder.ViewModels
{
    public partial class ConnectorViewModel : ObservableObject
    {
        [ObservableProperty]
        private Point anchor;

        [ObservableProperty]
        private bool isConnected;

        public string Title { get; set; } = string.Empty;
    }
}
