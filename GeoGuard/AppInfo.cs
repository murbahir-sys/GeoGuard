using System.Reflection;

namespace GeoGuard;

/// <summary>Сведения о программе: имя, версия, автор. Версия берётся из сборки (GeoGuard.csproj).</summary>
internal static class AppInfo
{
    public const string Name = "GeoGuard";
    public const string Author = "Савицкий Святослав";
    public const string Tagline = "Приложения работают только в выбранной стране";
    public const string Description =
        "GeoGuard следит за внешним IP и разрешает выбранным приложениям работать только в одной стране. " +
        "В любой другой стране, а также пока страна не подтверждена, эти приложения закрываются, " +
        "а с правами администратора ещё и теряют доступ к сети вне VPN.";

    /// <summary>Страница программы с исходным кодом и релизами.</summary>
    public const string RepositoryUrl = "https://github.com/murbahir-sys/GeoGuard";

    public static string Copyright => $"© 2026 {Author}";

    public static Version Version => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

    /// <summary>Например, «1.0.0».</summary>
    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Version.Build}";
}
