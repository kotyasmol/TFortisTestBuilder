using TestBuilder.Services;

namespace TestBuilder.Tests.Services;

public class NetworkPresetCatalogTests
{
    [Fact]
    public void BundledPresetsAreExternalDataAndHaveCompletePairs()
    {
        var catalog = NetworkPresetCatalog.LoadBundled();
        Assert.True(catalog.Count >= 3);
        Assert.Equal(4, catalog.Single(p => p.Id == "four-100").Plan.Addresses.Count);
        Assert.Equal(4, catalog.Single(p => p.Id == "four-1000").Plan.Addresses.Count);
        Assert.Equal(10, catalog.Single(p => p.Id == "eight-100-two-1000").Plan.Addresses.Count);
        foreach (var preset in catalog) { preset.Validate(); Assert.NotEmpty(preset.Plan.BuildDataTestPairs()); }
        Assert.All(catalog.Single(p => p.Id == "four-1000").Plan.Addresses, p => Assert.Equal(1000, p.TestMbps));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"presets\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"presets\":null}")]
    [InlineData("{\"schemaVersion\":1,\"presets\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"presets\":[{\"id\":\"x\",\"name\":\"X\",\"plan\":null}]}")]
    public void InvalidOrFutureFilesAreRejectedBeforeChangingSettings(string json) =>
        Assert.ThrowsAny<Exception>(() => NetworkPresetCatalog.Read(json));
}
