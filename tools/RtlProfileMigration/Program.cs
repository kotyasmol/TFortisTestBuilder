// Offline source-diagram conversion, not a runtime dependency. No production hardware access.
using System.Text.Json;
using System.Xml.Linq;
using SkiaSharp;
using TestBuilder.Models;
if (args.Length != 2) throw new ArgumentException("Usage: RtlProfileMigration <rtl_poe_v3.svg> <board.json>");
var xml = XDocument.Load(args[0]);
var width = 1376.748f; var height = 605.39961f;
using var bitmap = new SKBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height));
using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White); canvas.Translate(.499977f, -517.62011f);
using var paint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = .7f, IsAntialias = true };
var regions = new List<BoardRegion>();
foreach (var element in xml.Descendants().Where(e => e.Name.LocalName == "path"))
{
    var d = (string?)element.Attribute("d"); if (d == null) continue;
    using var path = SKPath.ParseSvgPathData(d); if (path == null) continue;
    canvas.DrawPath(path, paint);
    var id = (string?)element.Attribute("id") ?? "";
    if (!(id.StartsWith("Poe") || id.StartsWith("Led") || id is "3V3" or "Spi_swd_uart93")) continue;
    // Preserve disjoint component areas rather than a single box spanning the board.
    using var measure = new SKPathMeasure(path, false);
    var index = 0;
    do
    {
        using var contour = new SKPath();
        if (!measure.GetSegment(0, measure.Length, contour, true)) continue;
        var b = contour.Bounds; if (b.Width < 3 || b.Height < 3) continue;
        var x = Math.Clamp((b.Left + .499977f) / width, 0, 1); var y = Math.Clamp((b.Top - 517.62011f) / height, 0, 1);
        var w = Math.Min(b.Width / width, 1 - x); var h = Math.Min(b.Height / height, 1 - y);
        if (w <= 0 || h <= 0) continue;
        regions.Add(new BoardRegion { Id = id + "_" + (++index), Name = id, X = x, Y = y, Width = w, Height = h });
    } while (measure.NextContour());
}
using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
var board = new BoardDesign { ImageBase64 = Convert.ToBase64String(data.ToArray()), Regions = regions };
board.Validate();
File.WriteAllText(args[1], JsonSerializer.Serialize(board));
Console.WriteLine($"Converted source diagram: {regions.Count} editable component areas.");
