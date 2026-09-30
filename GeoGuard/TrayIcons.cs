using System.Drawing.Drawing2D;

namespace GeoGuard;

/// <summary>Иконки трея рисуются кодом, чтобы не таскать ресурсы: «глобус» разного цвета для каждого состояния.</summary>
internal static class TrayIcons
{
    public static Icon Unknown { get; } = Create(Color.FromArgb(128, 128, 128));
    public static Icon Allowed { get; } = Create(Color.FromArgb(46, 160, 67));
    public static Icon Blocking { get; } = Create(Color.FromArgb(215, 58, 73));

    public static Icon For(GuardState state) => state switch
    {
        GuardState.Allowed => Allowed,
        GuardState.Blocking => Blocking,
        _ => Unknown,
    };

    private static Icon Create(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 1, 1, 30, 30);

            using var pen = new Pen(Color.White, 2.5f);
            g.DrawEllipse(pen, 9, 3, 14, 26);   // меридиан
            g.DrawLine(pen, 3, 16, 29, 16);     // экватор
        }

        // Три иконки живут до конца процесса, поэтому дескриптор намеренно не освобождается.
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
