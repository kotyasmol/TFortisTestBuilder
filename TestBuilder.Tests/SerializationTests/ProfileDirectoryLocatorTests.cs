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
}
