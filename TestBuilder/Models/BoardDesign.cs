using System;
using System.Collections.Generic;
using System.Linq;

namespace TestBuilder.Models;
public sealed class BoardRegion
{
    public string Id { get; set; } = "element";
    public string Name { get; set; } = "Элемент";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 0.1;
    public double Height { get; set; } = 0.1;
    public override string ToString() => $"{Id} — {Name}";
}
public sealed class BoardDesign
{
    public string ImageBase64 { get; set; } = "";
    public List<BoardRegion> Regions { get; set; } = new();
    public void Validate()
    {
        if (ImageBase64 == null || ImageBase64.Length > 14_000_000) throw new InvalidOperationException("Изображение платы слишком велико (до 10 МБ).");
        if (ImageBase64.Length > 0) { try { BoardImage.Validate(Convert.FromBase64String(ImageBase64)); } catch (FormatException) { throw new InvalidOperationException("Повреждено изображение платы."); } }
        if (Regions == null || Regions.Count > 1000 || Regions.Any(r => r == null) ||
            Regions.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != Regions.Count)
            throw new InvalidOperationException("Нужно не более 1000 областей с уникальными ID.");
        foreach (var r in Regions)
            if (string.IsNullOrWhiteSpace(r.Id) || r.Id.Contains(',') || r.Id.Contains(';') ||
                !double.IsFinite(r.X + r.Y + r.Width + r.Height) || r.X < 0 || r.Y < 0 || r.Width <= 0 || r.Height <= 0 ||
                r.X + r.Width > 1.000001 || r.Y + r.Height > 1.000001)
                throw new InvalidOperationException($"Область {r.Id}: задайте ID и координаты в пределах 0..1.");
    }
}
