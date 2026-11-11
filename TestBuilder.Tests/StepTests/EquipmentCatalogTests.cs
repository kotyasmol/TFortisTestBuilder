using System.Text;
using TestBuilder.Services;

namespace TestBuilder.Tests.StepTests;

public class EquipmentCatalogTests
{
    [Theory]
    [InlineData(0, "Fibox", "002600579")]
    [InlineData(1, "Plastim", "102600579")]
    [InlineData(3, "Наш шкаф", "302600579")]
    public void Model026UsesOneCatalogEntryForSerialMacAndCabinet(int variant, string cabinet, string barcode)
    {
        var model = EquipmentCatalog.LoadBundled().Resolve("psw-2g8f-plus-box", null)!;
        Assert.True(EquipmentCatalog.Matches(model, "026"));
        Assert.Equal("PSW-2G8F+Box", model.EffectiveServerDeviceType);
        var series = model.SerialSeries!;
        Assert.Equal(2600001, series.FirstSerialNumber);
        Assert.Equal(2665535, series.LastSerialNumber);
        Assert.Null(series.Check(2600579));
        Assert.NotNull(series.Check(3200579));
        Assert.Null(series.CheckMac("C0:11:A6:1A:02:43", 2600579));
        var template = LabelPrintContent.ForModel(LabelTemplateCatalog.Defaults()[0], model, variant);
        Assert.Equal(barcode, template.Barcode(579));
        var command = LabelPrintContent.Render(template, "2600579", 579, "C0:11:A6:1A:02:43");
        var preview = LabelPreview.Parse(template, command);
        Assert.Null(preview.BarcodeError);
        Assert.Contains(preview.Elements, element => element.Text == cabinet);
        Assert.Contains(preview.Elements, element => element.Text == "SN: 2600579");
        var bars = Assert.Single(preview.Elements.Where(element => element.Bars.Length > 0));
        Assert.Equal(Code128Encoding.EncodeAuto(barcode), bars.Bars);
        var withoutCabinet = LabelPreview.Parse(template, LabelPrintContent.Render(
            LabelPrintContent.ForModel(template, model with { LabelCabinets = null }, variant), "2600579", 579, "C0:11:A6:1A:02:43"));
        Assert.Equal(withoutCabinet.Message, preview.Message);
        foreach (var text in preview.Elements.Where(element => element.Text == cabinet || element.Text.StartsWith("SN:")))
        {
            Assert.True(text.X >= preview.Left && text.X + text.Width <= preview.Left + preview.Width);
            Assert.True(text.Y + text.Height <= bars.Y + 3);
        }
        Assert.Equal(Assert.Single(withoutCabinet.Elements.Where(element => element.Bars.Length > 0)), bars);
        // The real printer transport uses CP1251; UTF-8 hex fields must survive it losslessly.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var wire = Encoding.GetEncoding(1251).GetString(Encoding.GetEncoding(1251).GetBytes(command));
        Assert.Equal(preview.Elements, LabelPreview.Parse(template, wire).Elements);
    }

    [Fact]
    public void CatalogChangesArePortableAndAnUnknownModelDoesNotInheritASerialSeries()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var moved = folder + "-moved";
        try
        {
            var model = new EquipmentModel("custom", "Новое изделие")
            {
                LabelCode = 126, LabelVariants = new() { [2] = "Корпус и питание" },
                LabelCabinets = new() { [2] = "Cabinet" }, IsFullOverride = true
            };
            EquipmentCatalog.SaveModel(folder, model, createNew: true);
            Directory.CreateDirectory(Path.GetDirectoryName(EquipmentCatalog.LocalPath(moved))!);
            File.Copy(EquipmentCatalog.LocalPath(folder), EquipmentCatalog.LocalPath(moved));
            var restored = EquipmentCatalog.Load(moved).Resolve(model.Id, null)!;
            Assert.Equal("Cabinet", restored.LabelCabinets![2]);
            Assert.Null(restored.SerialSeries);
            Assert.Null(restored.EffectiveServerDeviceType);
            EquipmentCatalog.SaveModel(moved, restored with { LabelCabinets = new() }, original: restored);
            Assert.Empty(EquipmentCatalog.Load(moved).Resolve(model.Id, null)!.LabelCabinets!);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            if (Directory.Exists(moved)) Directory.Delete(moved, true);
        }
    }

    [Fact]
    public void InvalidSeriesNamesTheIncorrectField()
    {
        var series = EquipmentCatalog.LoadBundled().Resolve("psw-2g8f-plus-box", null)!.SerialSeries!;
        Assert.Contains("меньше стартового", Assert.Throws<InvalidDataException>(() =>
            (series with { LastSerialNumber = 2600000 }).Validate("Test")).Message);
        Assert.Contains("База короткого номера", Assert.Throws<InvalidDataException>(() =>
            (series with { SerialOffset = 0 }).Validate("Test")).Message);
        Assert.Contains("четыре байта", Assert.Throws<InvalidDataException>(() =>
            (series with { MacPrefix = "C0:11:A6:1A:00:00" }).Validate("Test")).Message);
    }
}
