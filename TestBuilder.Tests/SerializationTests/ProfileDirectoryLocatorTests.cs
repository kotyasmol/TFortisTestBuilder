using TestBuilder.Services;

namespace TestBuilder.Tests.SerializationTests;

public class ProfileDirectoryLocatorTests
{
    [Fact]
    public void FindProjectProfilesDirectory_FindsProfilesFromNestedBuildFolder()
    {
        var root = Directory.CreateTempSubdirectory("testbuilder-profiles-");

        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "TestBuilder.slnx"), "<Solution />");
            var profiles = Directory.CreateDirectory(Path.Combine(root.FullName, "profiles"));
            var buildFolder = Directory.CreateDirectory(
                Path.Combine(root.FullName, "TestBuilder", "bin", "Debug", "net8.0"));

            var result = ProfileDirectoryLocator.FindProjectProfilesDirectory(buildFolder.FullName);

            Assert.Equal(profiles.FullName, result);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void FindProjectProfilesDirectory_RequiresSolutionMarker()
    {
        var root = Directory.CreateTempSubdirectory("testbuilder-profiles-");

        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "profiles"));

            var result = ProfileDirectoryLocator.FindProjectProfilesDirectory(root.FullName);

            Assert.Null(result);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void PortableTemplatesOnlySeedMissingFiles()
    {
        var root = Directory.CreateTempSubdirectory("testbuilder-profiles-");
        try
        {
            var templates = Directory.CreateDirectory(Path.Combine(root.FullName, "profiles", "standard"));
            var working = Directory.CreateDirectory(Path.Combine(root.FullName, "profiles"));
            var edited = Path.Combine(working.FullName, "existing.json");
            var seed = Path.Combine(templates.FullName, "new.json");
            File.WriteAllText(edited, "edited locally");
            File.WriteAllText(Path.Combine(templates.FullName, "existing.json"), "bundled");
            File.WriteAllText(seed, "new bundled file");
            File.SetLastWriteTimeUtc(seed, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var oldDate = File.GetLastWriteTimeUtc(edited);

            Assert.Equal(working.FullName, ProfileDirectoryLocator.ResolvePortableDirectory(root.FullName));
            Assert.Equal("edited locally", File.ReadAllText(edited));
            Assert.Equal(oldDate, File.GetLastWriteTimeUtc(edited));
            var added = Path.Combine(working.FullName, "new.json");
            Assert.Equal("new bundled file", File.ReadAllText(added));
            Assert.Equal(File.GetLastWriteTimeUtc(seed), File.GetLastWriteTimeUtc(added));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void SettingsWithLegacyPortablePathSeedMissingProfilesAndPreserveEdits()
    {
        var root = Directory.CreateTempSubdirectory("testbuilder-profiles-");
        try
        {
            var templates = Directory.CreateDirectory(Path.Combine(root.FullName, "profiles", "standard"));
            var working = Directory.CreateDirectory(Path.Combine(root.FullName, "profiles"));
            var custom = Directory.CreateDirectory(Path.Combine(root.FullName, "custom-profiles"));
            var edited = Path.Combine(working.FullName, "existing.json");
            File.WriteAllText(edited, "edited locally");
            File.WriteAllText(Path.Combine(templates.FullName, "existing.json"), "bundled");
            File.WriteAllText(Path.Combine(templates.FullName, "new.json"), "new bundled file");

            var file = Path.Combine(root.FullName, "testbuilder.settings");
            var legacyPath = Path.Combine(root.FullName, ".", "profiles") + Path.DirectorySeparatorChar;
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(new { graphsFolder = legacyPath }));

            var settings = AppSettings.Load(file);
            Assert.Equal(legacyPath, settings.GraphsFolder);
            settings.GraphsFolder = custom.FullName;
            Assert.Equal(custom.FullName, settings.ResolvedGraphsFolder);
            Assert.Empty(custom.GetFiles());
            var added = Path.Combine(working.FullName, "new.json");
            Assert.False(File.Exists(added));

            settings.GraphsFolder = legacyPath;
            Assert.Equal(working.FullName, settings.ResolvedGraphsFolder);
            Assert.Equal("edited locally", File.ReadAllText(edited));
            Assert.Equal("new bundled file", File.ReadAllText(added));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void UnavailableConfiguredFolderAndCorruptSettingsArePreserved()
    {
        var root = Directory.CreateTempSubdirectory("testbuilder-settings-");
        try
        {
            var file = Path.Combine(root.FullName, "testbuilder.settings");
            var missing = Path.Combine(root.FullName, "offline-share");
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(new { graphsFolder = missing }));
            Assert.Equal(missing, AppSettings.Load(file).GraphsFolder);

            File.WriteAllText(file, "{ corrupt");
            var settings = AppSettings.Load(file);
            Assert.NotEmpty(settings.LoadError);
            Assert.Throws<IOException>(settings.SaveChecked);
            Assert.Equal("{ corrupt", File.ReadAllText(file));
        }
        finally { root.Delete(recursive: true); }
    }
}
