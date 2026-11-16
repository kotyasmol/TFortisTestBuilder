using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace TestBuilder.Domain.Steps;

internal static class DataTestAdapterBinding
{
    internal static T? Resolve<T>(IEnumerable<T> source, string interfaceId, PhysicalAddress mac,
        Func<T, string> name, Func<T, PhysicalAddress> address) where T : class
    {
        var devices = source.ToArray();
        var id = interfaceId.Trim('{', '}');
        var byId = devices.Where(d => name(d).Replace("{", "").Replace("}", "")
            .Equals(id, StringComparison.OrdinalIgnoreCase) ||
            Guid.TryParse(id, out _) && name(d).Replace("{", "").Replace("}", "").EndsWith("_" + id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (byId.Length == 1) return byId[0];
        if (byId.Length > 1) throw new InvalidOperationException($"Несколько pcap-интерфейсов с ID {interfaceId}.");
        var byMac = mac.GetAddressBytes().Length == 6
            ? devices.Where(d => address(d).Equals(mac)).ToArray() : Array.Empty<T>();
        if (byMac.Length > 1)
            throw new InvalidOperationException($"MAC {mac} соответствует нескольким pcap-интерфейсам. Нужна однозначная привязка к ID {interfaceId}.");
        return byMac.SingleOrDefault();
    }
}
