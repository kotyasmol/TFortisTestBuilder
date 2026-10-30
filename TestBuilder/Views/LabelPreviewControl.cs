using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TestBuilder.Services;

namespace TestBuilder.Views;

public sealed class LabelPreviewControl : Control
{
    private static readonly Typeface PreviewTypeface = new("avares://Avalonia.Fonts.Inter/Assets#Inter");
    public static readonly StyledProperty<LabelPreview?> PreviewProperty =
        AvaloniaProperty.Register<LabelPreviewControl, LabelPreview?>(nameof(Preview));
    public LabelPreview? Preview { get => GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    static LabelPreviewControl() => AffectsRender<LabelPreviewControl>(PreviewProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Preview is not { HasContent: true } preview || Bounds.Width < 4 || Bounds.Height < 4) return;
        var scale = System.Math.Min((Bounds.Width - 4) / preview.Width, (Bounds.Height - 4) / preview.Height);
        var paper = new Rect((Bounds.Width - preview.Width * scale) / 2, (Bounds.Height - preview.Height * scale) / 2,
            preview.Width * scale, preview.Height * scale);
        context.DrawRectangle(Brushes.White, new Pen(Brushes.Silver, 1), paper, 3, 3);
        using (context.PushClip(paper))
        {
            foreach (var element in preview.Elements)
            {
                var rect = new Rect(paper.X + (element.X - preview.Left) * scale, paper.Y + (element.Y - preview.Top) * scale,
                    element.Width * scale, element.Height * scale);
                if (element.Bars.Length > 0)
                {
                    var module = rect.Width / element.Bars.Sum(c => c - '0'); var x = rect.X;
                    for (var i = 0; i < element.Bars.Length; i++)
                    {
                        var width = (element.Bars[i] - '0') * module;
                        if (i % 2 == 0) context.FillRectangle(Brushes.Black, new Rect(x, rect.Y, width, rect.Height));
                        x += width;
                    }
                }
                else if (element.Text.Length > 0)
                {
                    var text = new FormattedText(element.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        PreviewTypeface, rect.Height, Brushes.Black);
                    if (text.Width <= 0 || text.Height <= 0) continue;
                    using (context.PushTransform(Matrix.CreateScale(rect.Width / text.Width, rect.Height / text.Height)
                        * Matrix.CreateTranslation(rect.X, rect.Y)))
                        context.DrawText(text, new Point());
                }
                else if (element.Stroke > 0)
                    context.DrawRectangle(null, new Pen(Brushes.Black, element.Stroke * scale), rect);
            }
        }
    }
}
