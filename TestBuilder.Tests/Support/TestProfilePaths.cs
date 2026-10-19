namespace TestBuilder.Tests.Support;

internal static class TestProfilePaths
{
    internal static string Find(string file)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var production = Path.Combine(root, "profiles", file);
        return File.Exists(production) ? production : Path.Combine(root, "examples", "profiles", file);
    }
}
