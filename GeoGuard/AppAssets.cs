using System.Reflection;

namespace GeoGuard;

/// <summary>Иконка и логотип программы из встроенных ресурсов.</summary>
internal static class AppAssets
{
    private static readonly Lazy<Image> LogoImage = new(() =>
    {
        using var stream = OpenResource("Logo.png");
        using var original = Image.FromStream(stream);
        return new Bitmap(original); // копия, не привязанная к потоку
    });

    public static Image Logo => LogoImage.Value;

    /// <summary>Иконка приложения нужного размера (берётся ближайший кадр из .ico).</summary>
    public static Icon Icon(int size)
    {
        using var stream = OpenResource("GeoGuard.ico");
        return new Icon(stream, new Size(size, size));
    }

    private static Stream OpenResource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Ресурс {name} не найден в сборке.");
}
