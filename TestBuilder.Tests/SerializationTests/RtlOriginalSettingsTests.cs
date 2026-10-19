using System.Text.Json;

namespace TestBuilder.Tests.SerializationTests;

public class RtlOriginalSettingsTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
    private static JsonDocument Read(string path) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, path)));
    private static IEnumerable<JsonElement> Nodes(JsonElement graph)
    {
        foreach (var node in graph.GetProperty("nodes").EnumerateArray())
        {
            yield return node;
            foreach (var key in new[] { "body", "bodyGraph" })
                if (node.TryGetProperty(key, out var body))
                    foreach (var child in Nodes(body)) yield return child;
        }
    }
    private static string Text(JsonElement node, string key) => node.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";

    [Fact]
    public void SwPreparationWritesOriginalRawDelaysAndEveryLimit()
    {
        using var original = Read("tools/RtlProfileMigration/SourceProfiles/RTL_SW_v1.json");
        using var profile = Read("profiles/RTL_SW_board.json");
        var source = original.RootElement;
        var nodes = Nodes(profile.RootElement).ToArray();
        var writes = nodes.Where(n => Text(n, "type") == "Write Register").ToArray();
        void Check(int address, string key) => Assert.Equal(source.GetProperty(key).GetInt32(),
            Assert.Single(writes.Where(n => n.GetProperty("address").GetInt32() == address)).GetProperty("value").GetInt32());
        Check(2329, "k5_test_delay"); Check(2332, "vcc_start_delay");
        // In particular, 10 is a raw controller value, not 2000 ms.
        Assert.Equal(10, source.GetProperty("k5_test_delay").GetInt32());
        foreach (var (address, key) in new (int, string)[] {
            (2333,"k5_52V"), (2336,"k5_55V"), (2339,"vout_vmain"), (2341,"vout_vres"),
            (2344,"vref"), (2347,"v12"), (2350,"vcc_3V3"), (2353,"vcc_1V5"), (2356,"vcc_1V1"),
            (2359,"cr2032"), (2362,"cr2032_cpu"), (2365,"dut_tamper_status"), (2368,"dut_tamper_led") })
        { Check(address, key + "_min"); Check(address + 1, key + "_max"); }
        foreach (var (address, key) in new[] { (2317, "cr2032_NoPWR"), (2318, "cr2032_NoPWR_cpu") })
        {
            var check = Assert.Single(nodes.Where(n => Text(n,"type") == "Check Register Range" && n.GetProperty("address").GetInt32() == address));
            Assert.Equal(source.GetProperty(key + "_min").GetInt32(), check.GetProperty("min").GetInt32());
            Assert.Equal(source.GetProperty(key + "_max").GetInt32(), check.GetProperty("max").GetInt32());
        }
        foreach (var (variable, key) in new[] { ("PoeFirmware","firmware"), ("PoeMcu","mcu"), ("FirmwareVersion","firmware_vers"), ("McuVersion","firmware_swd_vers") })
            Assert.Equal(source.GetProperty(key).GetString(), Text(Assert.Single(nodes.Where(n => Text(n,"type") == "Check Variable Equality" && Text(n,"variableName") == variable)), "expectedValue"));
        Assert.False(Assert.Single(nodes.Where(n => Text(n,"name").StartsWith("TAMPER"))).GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public void PoeUsesOriginalRevisionColorPortSelectionAndVoltageLimits()
    {
        using var original = Read("tools/RtlProfileMigration/SourceProfiles/RTL_POE_v1.json");
        using var profile = Read("profiles/RTL_POE_board.json");
        var source = original.RootElement;
        var nodes = Nodes(profile.RootElement).ToArray();
        foreach (var node in nodes.Where(n => Text(n,"type") == "Check Register Range"))
        {
            var address = node.GetProperty("address").GetInt32();
            var keys = address switch { 2404 => ("3.3VMin", "3.3Vmax"), 2486 => ("board_version", "board_version"),
                >= 2495 and <= 2510 => ($"uart_ch{address - 2494}_voltage_min", $"uart_ch{address - 2494}_voltage_max"), _ => ("", "") };
            if (keys.Item1 == "") continue;
            Assert.Equal(source.GetProperty(keys.Item1).GetInt32(), node.GetProperty("min").GetInt32());
            Assert.Equal(source.GetProperty(keys.Item2).GetInt32(), node.GetProperty("max").GetInt32());
        }
        Assert.All(nodes.Where(n => Text(n,"type") == "Check LED Color"), n =>
            Assert.Equal(source.GetProperty("led_colour").GetString(), n.GetProperty("checkLedColor").GetProperty("expectedColor").GetString()));
        for (var port = 1; port <= 8; port++)
            Assert.Equal(source.GetProperty($"port{port}_test").GetBoolean(), Assert.Single(nodes.Where(n => Text(n,"name") == $"Подача PoE порт {port}")).GetProperty("isEnabled").GetBoolean());
        var reset = Assert.Single(nodes.Where(n => Text(n,"name") == "Питание и reset"));
        Assert.Equal(source.GetProperty("startup_time").GetInt32() * 1000,
            Nodes(reset.GetProperty("bodyGraph")).Last(n => Text(n,"type") == "Delay").GetProperty("milliseconds").GetInt32());
    }

    [Theory]
    [InlineData("SW"), InlineData("POE")]
    public void OriginalProfilesDoNotEnableReportsPrintingOrAutomaticFlashing(string board)
    {
        using var profile = Read($"profiles/RTL_{board}_board.json");
        var nodes = Nodes(profile.RootElement).ToArray();
        Assert.All(nodes.Where(n => Text(n,"name").StartsWith("Отчёт") || Text(n,"name").StartsWith("Этикетка")),
            n => Assert.False(n.GetProperty("isEnabled").GetBoolean()));
        Assert.DoesNotContain(nodes, n => Text(n,"type") == "Run Program" && !n.GetProperty("runProgram").GetProperty("programPath").GetString()!.StartsWith("read_"));
        Assert.Equal(1000, profile.RootElement.GetProperty("hardware").GetProperty("PollIntervalMs").GetInt32());
    }
}
