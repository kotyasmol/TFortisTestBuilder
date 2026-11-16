using System.Text.Json;
using TestBuilder.Services;

namespace TestBuilder.Tests.Services;

public class ProfileCurrentStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "profile-current-" + Guid.NewGuid().ToString("N"));
    private string StatePath => Path.Combine(_directory, "local", "current.json");
    private string Profile(string name) => Path.Combine(_directory, name + ".json");

    [Fact]
    public async Task SeveralChoicesPersistAndDoNotTravelToAnotherLocalStore()
    {
        var store = new ProfileCurrentStore(StatePath);
        Assert.Empty(store.Read()); Assert.False(Directory.Exists(_directory));
        await store.SetAsync(Profile("a"), true); await store.SetAsync(Profile("b"), true);
        Assert.Equal(2, new ProfileCurrentStore(StatePath).Read().Count);
        Assert.Empty(new ProfileCurrentStore(Path.Combine(_directory, "another-pc", "current.json")).Read());
        await store.SetAsync(Profile("a"), false);
        Assert.Equal(new[] { Profile("b") }, store.Read());
    }

    [Fact]
    public async Task SeparateInstancesPreserveConcurrentChoicesAndRenameOnlyTheChosenPath()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => new ProfileCurrentStore(StatePath).SetAsync(Profile(i.ToString()), true)));
        var store = new ProfileCurrentStore(StatePath);
        Assert.Equal(8, store.Read().Count);
        await store.MoveAsync(Profile("0"), Profile("renamed"));
        var values = store.Read();
        Assert.Equal(8, values.Count); Assert.DoesNotContain(Profile("0"), values); Assert.Contains(Profile("renamed"), values);
        Assert.DoesNotContain(Profile("copy"), values);
    }

    [Fact]
    public async Task CorruptOrFutureStateIsNotSilentlyOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        foreach (var text in new[] { "invalid-json", "{\"schemaVersion\":2,\"profiles\":[]}" })
        {
            await File.WriteAllTextAsync(StatePath, text);
            await Assert.ThrowsAsync<JsonException>(() => new ProfileCurrentStore(StatePath).SetAsync(Profile("a"), true));
            Assert.Equal(text, await File.ReadAllTextAsync(StatePath));
        }
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
