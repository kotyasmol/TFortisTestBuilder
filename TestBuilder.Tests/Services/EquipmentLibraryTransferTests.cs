using System.Text.Json;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;

namespace TestBuilder.Tests.Services;

public class EquipmentLibraryTransferTests
{
    private static DeviceDefinition Device(string name) => new() { Name = name, SlaveId = 17,
        Registers = new() { new RegisterDefinition { Address = 10, Name = "PoeMode", Writable = true, RetryWriteOnMismatch = true } } };

    [Fact]
    public void TransferMergesCustomTemplatesAndPreservesLocalEntriesAndIndependentInstances()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(root);
        try
        {
            var source = new EquipmentLibrary(Path.Combine(root, "source.json"));
            var target = new EquipmentLibrary(Path.Combine(root, "target.json"));
            var first = source.Save(Device("Прибор"));
            var instance = EquipmentLibrary.Instantiate(first, new byte[] { 1 });
            var untouched = target.Save(Device("Местный прибор"));
            var original = JsonSerializer.Serialize(instance);
            var preview = target.PreviewImport(source.ExportJson());
            Assert.Equal(1, preview.Added); Assert.Equal(0, preview.Updated);
            Assert.Single(target.Load().Where(e => !e.IsBuiltIn)); // Preview never writes.
            target.ApplyImport(preview);
            Assert.Contains(target.Load(), e => e.Id == untouched.Id);
            Assert.Equal(1, target.Load().Single(e => e.Id == first.Id).Device.SlaveId);
            var edited = first.Device; edited.Registers[0].Address = 15; source.Save(edited, first.Id);
            preview = target.PreviewImport(source.ExportJson());
            Assert.Equal(0, preview.Added); Assert.Equal(1, preview.Updated);
            Assert.Contains("Обновить: Прибор", preview.Changes);
            target.ApplyImport(preview);
            Assert.Equal(15, target.Load().Single(e => e.Id == first.Id).Device.Registers[0].Address);
            Assert.Equal(original, JsonSerializer.Serialize(instance));
            Assert.Equal(1, target.PreviewImport(source.ExportJson()).Unchanged);
            Assert.DoesNotContain("builtin:", source.ExportJson());
            Assert.All(target.Load().Where(e => e.IsBuiltIn), e => Assert.StartsWith("builtin:", e.Id));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidOrStaleImportCannotReplaceTheCatalogAndLegacyLocalFileIsAccepted()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(root);
        try
        {
            var library = new EquipmentLibrary(Path.Combine(root, "catalog.json")); library.Save(Device("Местный"));
            var incoming = new[] { new EquipmentTemplate(Guid.NewGuid().ToString(), Device("Входящий")) };
            var json = JsonSerializer.Serialize(incoming); var bytes = File.ReadAllBytes(library.StoragePath);
            var preview = library.PreviewImport(json);
            Assert.Equal(bytes, File.ReadAllBytes(library.StoragePath));
            Assert.Throws<InvalidOperationException>(() => library.PreviewImport("{\"SchemaVersion\":2,\"Entries\":[]}"));
            Assert.Throws<InvalidOperationException>(() => library.PreviewImport(JsonSerializer.Serialize(new[] { incoming[0], incoming[0] })));
            var conflict = new EquipmentTemplate(Guid.NewGuid().ToString(), Device("Местный"));
            Assert.Throws<InvalidOperationException>(() => library.PreviewImport(JsonSerializer.Serialize(new[] { conflict })));
            Assert.Equal(bytes, File.ReadAllBytes(library.StoragePath));
            library.Save(Device("Добавленный позднее")); bytes = File.ReadAllBytes(library.StoragePath);
            Assert.Throws<InvalidOperationException>(() => library.ApplyImport(preview));
            Assert.Equal(bytes, File.ReadAllBytes(library.StoragePath));
            library.ApplyImport(library.PreviewImport(json));
            Assert.Equal(3, library.Load().Count(e => !e.IsBuiltIn));
        }
        finally { Directory.Delete(root, true); }
    }
}
