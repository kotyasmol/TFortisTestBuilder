using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Monitoring
{
    public readonly record struct RegisterKey(byte SlaveId, int Address);
}
