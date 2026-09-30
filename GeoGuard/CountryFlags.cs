using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace GeoGuard;

/// <summary>
/// Флаги стран из встроенных ресурсов (Flags.xx.png, 320 px в ширину, настоящие пропорции; источник — flagpedia.net).
/// Рисуются со скруглёнными углами и тонкой рамкой. Флаг вписывается в отведённый прямоугольник с сохранением
/// пропорций (у Германии 5:3, у Швейцарии квадрат…). Для неизвестного кода рисуется нейтральная заглушка с глобусом.
/// </summary>
internal static class CountryFlags
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Originals = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<(string Code, int Width, int Height), Bitmap> Scaled = new();
    private static readonly ImageAttributes EdgeSafe = CreateEdgeSafeAttributes();

    public static bool Has(string? isoCode) => Load(isoCode) is not null;

    /// <summary>Пропорции флага (ширина / высота) или 4/3, если флага нет.</summary>
    public static double AspectRatio(string? isoCode) =>
        Load(isoCode) is { } image ? (double)image.Width / image.Height : 4.0 / 3.0;

    /// <summary>
    /// Рисует флаг внутри <paramref name="box"/>: вписывает с сохранением пропорций и центрирует.
    /// isoCode == null или флага нет — заглушка. <paramref name="shadow"/> — мягкая тень для крупного флага.
    /// </summary>
    public static void Draw(Graphics g, string? isoCode, Rectangle box, bool shadow = false)
    {
        var image = Load(isoCode);
        var target = Fit(image is null ? 4.0 / 3.0 : (double)image.Width / image.Height, box);

        var saved = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var radius = Math.Max(2f, target.Height / 12f);
            if (shadow)
                DrawShadow(g, target, radius);

            var frame = new RectangleF(target.X + 0.5f, target.Y + 0.5f, target.Width - 1f, target.Height - 1f);
            using var path = Theme.Rounded(frame, radius);

            if (image is not null)
            {
                var clipState = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                g.DrawImage(ScaledTo(isoCode!, image, target.Width, target.Height), target);
                g.Restore(clipState);
                using var border = new Pen(Color.FromArgb(shadow ? 90 : 70, 0, 0, 0), 1f);
                g.DrawPath(border, path);
            }
            else
            {
                DrawPlaceholder(g, path, target);
            }
        }
        finally
        {
            g.Restore(saved);
        }
    }

    /// <summary>Флаг в готовой прозрачной картинке (для меню в трее). Картинку освобождает вызывающий.</summary>
    public static Bitmap Render(string? isoCode, int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        Draw(g, isoCode, new Rectangle(0, 0, width, height));
        return bitmap;
    }

    /// <summary>Вписывает прямоугольник заданных пропорций в <paramref name="box"/> по центру.</summary>
    internal static Rectangle Fit(double aspect, Rectangle box)
    {
        var width = box.Width;
        var height = (int)Math.Round(width / aspect);
        if (height > box.Height)
        {
            height = box.Height;
            width = (int)Math.Round(height * aspect);
        }

        return new Rectangle(box.X + (box.Width - width) / 2, box.Y + (box.Height - height) / 2, Math.Max(1, width), Math.Max(1, height));
    }

    private static void DrawShadow(Graphics g, Rectangle target, float radius)
    {
        // Мягкая тень из нескольких полупрозрачных слоёв: GDI+ не умеет размывать.
        for (var i = 4; i >= 1; i--)
        {
            var rect = new RectangleF(target.X - i + 0.5f, target.Y - i + 3f, target.Width + 2 * i - 1f, target.Height + 2 * i - 1f);
            using var path = Theme.Rounded(rect, radius + i);
            using var brush = new SolidBrush(Color.FromArgb(14, 0, 0, 0));
            g.FillPath(brush, path);
        }
    }

    private static void DrawPlaceholder(Graphics g, GraphicsPath path, Rectangle bounds)
    {
        using var fill = new SolidBrush(Color.FromArgb(229, 231, 235));
        using var pen = new Pen(Color.FromArgb(156, 163, 175), 1f);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        var d = Math.Min(bounds.Width, bounds.Height) * 0.62f;
        var cx = bounds.X + bounds.Width / 2f;
        var cy = bounds.Y + bounds.Height / 2f;
        g.DrawEllipse(pen, cx - d / 2, cy - d / 2, d, d);
        g.DrawEllipse(pen, cx - d * 0.2f, cy - d / 2, d * 0.4f, d);
        g.DrawLine(pen, cx - d / 2, cy, cx + d / 2, cy);
    }

    /// <summary>
    /// Картинка нужного размера. Большое уменьшение (320 → 20 px) делаем ступенями вдвое,
    /// иначе GDI+ даёт «лесенку» на мелких деталях.
    /// </summary>
    private static Bitmap ScaledTo(string code, Bitmap original, int width, int height) =>
        Scaled.GetOrAdd((code.ToUpperInvariant(), width, height), _ =>
        {
            var current = original;
            var owns = false;
            while (current.Width / 2 >= width && current.Height / 2 >= height)
            {
                var half = Resize(current, current.Width / 2, current.Height / 2);
                if (owns)
                    current.Dispose();
                current = half;
                owns = true;
            }

            var result = Resize(current, width, height);
            if (owns)
                current.Dispose();
            return result;
        });

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        var result = new Bitmap(width, height);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, EdgeSafe);
        return result;
    }

    private static ImageAttributes CreateEdgeSafeAttributes()
    {
        var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY); // без светлой каймы по краям при уменьшении
        return attributes;
    }

    private static Bitmap? Load(string? isoCode)
    {
        if (isoCode is not { Length: 2 } || !char.IsAsciiLetter(isoCode[0]) || !char.IsAsciiLetter(isoCode[1]))
            return null;

        return Originals.GetOrAdd(isoCode, code =>
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Flags.{code.ToLowerInvariant()}.png");
            if (stream is null)
                return null;

            using var original = Image.FromStream(stream);
            return new Bitmap(original); // копия, не привязанная к потоку
        });
    }
}
