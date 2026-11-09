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
                ?? ResolvePortableDirectory(AppContext.BaseDirectory);
        }

        internal static string ResolvePortableDirectory(string installation)
        {
            var workingFolder = Path.Combine(installation, ProfilesFolderName);
            var templates = Path.Combine(installation, "profile-templates");
            try
            {
                if (!Directory.Exists(templates)) return workingFolder;
                foreach (var source in Directory.EnumerateFiles(templates, "*.json", SearchOption.AllDirectories))
                {
                    var destination = Path.Combine(workingFolder, Path.GetRelativePath(templates, source));
                    if (File.Exists(destination)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.Copy(source, temporary);
                        File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(source));
                        File.Move(temporary, destination, overwrite: false);
                    }
                    catch (IOException) when (File.Exists(destination)) { }
                    finally { File.Delete(temporary); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logging.DiagnosticLog.WriteException("Подготовка папки профилей", ex);
            }
            return workingFolder;
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
