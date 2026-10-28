using TestBuilder.Services;
namespace TestBuilder.Tests.SerializationTests;
public class ProfileDisplayNameTests
{
    [Fact]
    public void AllCaptionsUseRussianMetadataInsteadOfTechnicalName()
    {
        var p = new GraphProfile("legacy.json", "PSW_UPS_Box_8x2Pro_parallel_start", "PSW+UPS-Box 8x2Pro", "Параллельный старт");
        Assert.Equal("PSW+UPS-Box 8x2Pro · Параллельный старт", p.DisplayName);
        Assert.Equal(p.DisplayName, p.StationName);
        Assert.Equal(p.DisplayName, p.ToString());
        Assert.Equal("PSW_UPS_Box_8x2Pro_parallel_start", p.Name);
    }
    [Theory]
    [InlineData("Модель", "Модель · Проверка", "Модель · Проверка")]
    [InlineData("Модель", "Модель — Проверка", "Модель · Проверка")]
    [InlineData("Модель", "Модель", "Модель")]
    [InlineData(null, "Проверка", "Проверка")]
    public void ModelIsIncludedOnlyOnce(string? model, string description, string expected) =>
        Assert.Equal(expected, GraphProfile.ComposeName(model, description));
    [Fact]
    public void LegacyProfilesRetainFallbackWithoutRecursiveDisplayName()
    {
        Assert.Equal("PSW-2G6F+ · Полная проверка", new GraphProfile("x", "PSW_2G6F_plus_full_algorithm").DisplayName);
        Assert.Equal("Моя проверка", new GraphProfile("x", "Моя_проверка").DisplayName);
    }
}
