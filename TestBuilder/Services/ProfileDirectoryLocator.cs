using System;
using System.IO;

namespace TestBuilder.Services
{
    internal static class ProfileDirectoryLocator
    {
        private const string SolutionFileName = "TestBuilder.slnx";
        private const string ProfilesFolderName = "profiles";

        public static string Resolve()
        {
            return FindProjectProfilesDirectory(AppContext.BaseDirectory)
                ?? FindProjectProfilesDirectory(Environment.CurrentDirectory)
                ?? Path.Combine(AppContext.BaseDirectory, ProfilesFolderName);
        }

        internal static string? FindProjectProfilesDirectory(string startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath))
                return null;

            try
            {
                var directory = new DirectoryInfo(Path.GetFullPath(startPath));

                while (directory != null)
                {
                    var solutionPath = Path.Combine(directory.FullName, SolutionFileName);
                    var profilesPath = Path.Combine(directory.FullName, ProfilesFolderName);

                    if (File.Exists(solutionPath) && Directory.Exists(profilesPath))
                        return profilesPath;

                    directory = directory.Parent;
                }
            }
            catch (Exception ex) when (
                ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return null;
            }

            return null;
        }
    }
}
