using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TestBuilder.Domain.Modbus;

/// <summary>Shared built-in register maps for the equipment editor and discovery.</summary>
public static class HardwarePresets
{
    // Return fresh definitions so editing a preset cannot change later discovery.
    public static List<DeviceDefinition> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "reference", "modbus", "hardware-presets.json");
        if (File.Exists(path))
        {
            using var file = File.OpenRead(path);
            return Read(file);
        }
        using var resource = typeof(HardwarePresets).Assembly.GetManifestResourceStream("TestBuilder.hardware-presets.json")
            ?? throw new InvalidOperationException("Встроенные карты оборудования отсутствуют.");
        return Read(resource);
    }

    private static List<DeviceDefinition> Read(Stream source) =>
        JsonSerializer.Deserialize<List<DeviceDefinition>>(source)
        ?? throw new InvalidOperationException("Не удалось прочитать карты оборудования.");
}
