using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text.RegularExpressions;
using SharpPcap;

namespace TestBuilder.Services;

public sealed record DataTestRuntimeReport(string Summary, string Details);

// Read-only: the same probe on every workstation. It neither injects traffic
// nor installs drivers, changes addresses or needs a connected DUT.
internal static class DataTestRuntimeCheck
{
    internal static DataTestRuntimeReport Inspect()
    {
        var version = typeof(DataTestRuntimeCheck).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "неизвестна";
        var details = new List<string> { $"Сборка программы: {version}" };
        if (!OperatingSystem.IsWindows())
            return new("Проверка компонентов DataTest доступна в Windows.", string.Join(Environment.NewLine, details));
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            details.Add(admin ? "Запуск с правами администратора." :
                "Для очистки ARP и изменения адресов нужен запуск лаунчера от имени администратора.");
            var count = CaptureDeviceList.Instance.Count;
            using var process = Process.GetCurrentProcess();
            var modules = new List<string>();
            var npcap = false;
            foreach (ProcessModule module in process.Modules)
                if (module.ModuleName.Equals("wpcap.dll", StringComparison.OrdinalIgnoreCase) ||
                    module.ModuleName.Equals("Packet.dll", StringComparison.OrdinalIgnoreCase))
                {
                    modules.Add($"{module.FileName} · {module.FileVersionInfo.FileVersion}");
                    npcap |= IsNpcap(module.FileName, module.FileVersionInfo.ProductName, module.FileVersionInfo.FileDescription);
                }
            details.AddRange(modules);
            details.Add("Линки и скорость проверяются во время DataTest после включения изделия.");
            return Format(count, npcap, details);
        }
        catch (Exception ex)
        {
            details.Add("Не удалось открыть компоненты захвата: " + ex.Message);
            details.Add("Установите Npcap с официального сайта https://npcap.com/#download и заново запустите программу через лаунчер.");
            return new("Компоненты DataTest недоступны.", string.Join(Environment.NewLine, details));
        }
    }

    internal static bool IsNpcap(string path, string? product, string? description) =>
        new[] { path, product, description }.Any(s => s != null && Regex.IsMatch(s,
            @"(?<![a-z0-9])Npcap(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    internal static DataTestRuntimeReport Format(int adapters, bool npcap, IEnumerable<string> details)
    {
        var lines = details.ToList();
        if (adapters == 0)
        {
            lines.Add("Драйвер не вернул сетевые карты. Проверьте установку Npcap и права доступа, затем перезапустите лаунчер.");
            return new("Для DataTest не найдены сетевые карты захвата.", string.Join(Environment.NewLine, lines));
        }
        if (!npcap) lines.Add("Используется прежняя библиотека захвата. Рекомендуемый компонент для Windows — Npcap; он устанавливается отдельно от программы.");
        return new($"{(npcap ? "Npcap" : "Библиотека захвата")} доступна. Найдено карт: {adapters}. " +
            "Скорость и потери подтверждаются основным замером.", string.Join(Environment.NewLine, lines));
    }
}
