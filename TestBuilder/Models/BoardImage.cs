using System;
using SkiaSharp;

namespace TestBuilder.Models;

public static class BoardImage
{
    public static void Validate(byte[] bytes)
    {
        if (bytes.Length > 10_000_000) throw new InvalidOperationException("Максимальный размер изображения — 10 МБ.");
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec == null) throw new InvalidOperationException("Изображение повреждено или имеет неподдерживаемый формат.");
        var info = codec.Info;
        if (info.Width < 1 || info.Height < 1 || info.Width > 12000 || info.Height > 12000 ||
            (long)info.Width * info.Height > 24_000_000 ||
            info.Height > info.Width * 8L || info.Width > info.Height * 8L)
            throw new InvalidOperationException("Изображение: до 24 мегапикселей, сторона до 12000 пикселей, пропорции не более 8:1.");
    }
}
