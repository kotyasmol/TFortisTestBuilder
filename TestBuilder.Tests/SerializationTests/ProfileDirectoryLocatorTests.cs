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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableProfilesFolderIgnoresOldTemplates(bool explicitPath)
    {
        var root = Directory.CreateTempSubdirectory("testbuilder-profiles-");
        try
        {
            var templates = Directory.CreateDirectory(Path.Combine(root.FullName, "profiles", "standard"));
            var working = Directory.CreateDirectory(Path.Combine(root.FullName, "profiles"));
            var edited = Path.Combine(working.FullName, "existing.json");
            File.WriteAllText(edited, "edited locally");
            File.WriteAllText(Path.Combine(templates.FullName, "existing.json"), "bundled");
            File.WriteAllText(Path.Combine(templates.FullName, "new.json"), "new bundled file");
            var oldDate = File.GetLastWriteTimeUtc(edited);
            var file = Path.Combine(root.FullName, "testbuilder.settings");
            var configured = explicitPath ? Path.Combine(root.FullName, ".", "profiles") + Path.DirectorySeparatorChar : "";
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(new { graphsFolder = configured }));

            var settings = AppSettings.Load(file);
            Assert.Equal(explicitPath ? configured : working.FullName, settings.ResolvedGraphsFolder);
            Assert.Equal("edited locally", File.ReadAllText(edited));
            Assert.Equal(oldDate, File.GetLastWriteTimeUtc(edited));
            Assert.Equal(edited, Assert.Single(Directory.GetFiles(working.FullName, "*.json")));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void ConfiguredEmptyFolderWinsOverInstallationAndProjectProfiles()
    {
        var root = Directory.CreateTempSubdirectory("testbuilder-profiles-");
        try
        {
            var working = Directory.CreateDirectory(Path.Combine(root.FullName, "profiles"));
            var custom = Directory.CreateDirectory(Path.Combine(root.FullName, "custom-profiles"));
            File.WriteAllText(Path.Combine(root.FullName, "TestBuilder.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(working.FullName, "existing.json"), "profile");
            var installation = Directory.CreateDirectory(Path.Combine(root.FullName, "bin"));
            var portable = Directory.CreateDirectory(Path.Combine(installation.FullName, "profiles"));
            File.WriteAllText(Path.Combine(portable.FullName, "other.json"), "profile");

            Assert.Equal(custom.FullName, ProfileDirectoryLocator.Resolve(installation.FullName, custom.FullName));
            Assert.Empty(custom.GetFiles());
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
