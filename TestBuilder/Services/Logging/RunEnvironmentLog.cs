using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TestBuilder.Services.Logging;

internal static class RunEnvironmentLog
{
    internal static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    internal static void Write(ILogger logger, string? profilePath, Func<string> effectiveJson, string purpose, string standId)
    {
        var assembly = typeof(RunEnvironmentLog).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "неизвестна";
        logger.Info($"Среда запуска: ПК={Environment.MachineName}; сборка={version}; ОС={RuntimeInformation.OSDescription}; " +
            $"архитектура={RuntimeInformation.ProcessArchitecture}; .NET={Environment.Version}; Stand ID={standId}; назначение={purpose}.");
        if (string.IsNullOrWhiteSpace(standId) || standId == AppSettings.DefaultStandId)
            logger.Warning("Не задан индивидуальный Stand ID. Укажите его в настройках сервера; имя ПК записано в этом журнале.");
        try
        {
            var fileHash = profilePath != null && File.Exists(profilePath)
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(profilePath))).ToLowerInvariant() : "файл не сохранён";
            // Hash the effective in-memory graph separately: unsaved edits and
            // explicit local settings must not be mistaken for the disk version.
            logger.Info($"Профиль: путь={profilePath ?? "новый профиль"}; SHA-256 файла={fileHash}; " +
                $"SHA-256 графа запуска={HashText(effectiveJson())}.");
        }
        catch (Exception ex)
        {
            logger.Warning("Не удалось записать контрольную сумму профиля: " + ex.Message);
        }
    }
}
