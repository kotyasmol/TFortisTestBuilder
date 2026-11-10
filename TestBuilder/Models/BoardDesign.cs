using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace TestBuilder.Models;
public sealed class BoardRegion
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

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
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public string ImageBase64 { get; set; } = "";
    public List<BoardRegion> Regions { get; set; } = new();

    public static BoardDesign ReadReference(string path)
    {
        var design = JsonSerializer.Deserialize<BoardDesign>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Файл не содержит описание платы.");
        if (design.ExtensionData?.Remove("ImageFile", out var imageField) == true)
        {
            var file = imageField.ValueKind == JsonValueKind.String ? imageField.GetString() : null;
            if (string.IsNullOrWhiteSpace(file) || file.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
                !string.Equals(Path.GetExtension(file), ".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Укажите имя PNG рядом с файлом платы.");
            var imagePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, file);
            if (new FileInfo(imagePath).Length > 10_000_000)
                throw new InvalidOperationException("Максимальный размер изображения — 10 МБ.");
            design.ImageBase64 = Convert.ToBase64String(File.ReadAllBytes(imagePath));
        }
        design.Validate();
        if (string.IsNullOrEmpty(design.ImageBase64)) throw new InvalidOperationException("В шаблоне нет изображения платы.");
        return design;
    }

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
