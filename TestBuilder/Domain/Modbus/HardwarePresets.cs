using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Platform;

namespace TestBuilder.Domain.Modbus;

/// <summary>Shared built-in register maps for the equipment editor and discovery.</summary>
public static class HardwarePresets
{
    // Return fresh definitions so editing a preset cannot change later discovery.
    public static List<DeviceDefinition> Load()
    {
        using var resource = AssetLoader.Open(new Uri("avares://TestBuilder/Assets/hardware-presets.json"));
        return JsonSerializer.Deserialize<List<DeviceDefinition>>(resource)
            ?? throw new InvalidOperationException("Не удалось прочитать встроенные карты оборудования.");
    }
}
