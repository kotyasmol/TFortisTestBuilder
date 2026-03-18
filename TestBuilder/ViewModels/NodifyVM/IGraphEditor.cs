using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBuilder.ViewModels.NodifyVM
{
    public interface IGraphEditor
    {
        void Connect(ConnectorViewModel source, ConnectorViewModel target);
    }
}
