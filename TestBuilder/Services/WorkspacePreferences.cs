using System;
using System.Collections.Generic;

namespace TestBuilder.Services;

/// <summary>Local workspace preferences; never part of a test profile.</summary>
public sealed class WorkspacePreferences
{
    public string ProductFilter { get; set; } = "All";
    public string EngineerPage { get; set; } = "Editor";
    public bool ProfilesCollapsed { get; set; }
    public bool PaletteCollapsed { get; set; }
    public bool QuickActionsCollapsed { get; set; } = true;
    public double LogHeight { get; set; } = 160;
    public double BoardStagesWidth { get; set; } = 290;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
    public Dictionary<string, bool> ExpandedSections { get; set; } = new();

    internal void Normalize()
    {
        if (ProductFilter is not ("All" or "Board" or "Switch")) ProductFilter = "All";
        if (EngineerPage is not ("Editor" or "Registers" or "SelfTest" or "Network" or "Labels")) EngineerPage = "Editor";
        LogHeight = ValidSize(LogHeight, 160, 100, 4000);
        BoardStagesWidth = ValidSize(BoardStagesWidth, 290, 220, 4000);
        WindowWidth = ValidSize(WindowWidth, 1280, 1100, 10000);
        WindowHeight = ValidSize(WindowHeight, 800, 680, 10000);
        ExpandedSections ??= new();
    }

    private static double ValidSize(double value, double fallback, double min, double max) =>
        double.IsFinite(value) && value > 0 ? Math.Clamp(value, min, max) : fallback;
}
