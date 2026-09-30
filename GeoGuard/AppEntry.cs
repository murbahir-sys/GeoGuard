using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace GeoGuard;

/// <summary>
/// Запись списка приложений: либо имя процесса ("chrome"), либо полный путь к exe ("C:\...\chrome.exe").
/// Закрытие работает по имени процесса; правила файрвола — только там, где известен путь.
/// </summary>
public static class AppEntry
{
    /// <returns>Нормализованная запись или пустая строка, если запись некорректна.</returns>
    public static string Normalize(string? input)
    {
        var text = (input ?? "").Trim().Trim('"').Trim();
        if (text.Length == 0)
            return "";

        var looksLikePath = text.IndexOfAny(['\\', '/']) >= 0 || (text.Length >= 2 && text[1] == ':');
        if (!looksLikePath)
            return text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? text[..^4].TrimEnd() : text;

        // Относительные пути ("sub\a.exe", "C:a.exe") зависят от текущей папки — не принимаем.
        if (!Path.IsPathFullyQualified(text))
            return "";

        try
        {
            return Path.GetFullPath(text);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }

    public static bool IsPath(string entry) => Path.IsPathFullyQualified(entry);

    public static string ProcessName(string entry) => IsPath(entry) ? Path.GetFileNameWithoutExtension(entry) : entry;

    /// <summary>
    /// Пути к exe: для записи-пути — она сама, для имени — из реестра «App Paths» или из PATH.
    /// null, если найти не удалось (тогда файрвол для этого приложения не действует, работает только закрытие).
    /// </summary>
    public static string? ResolvePath(string entry)
    {
        if (IsPath(entry))
            return entry;

        // Вызывается по таймеру, а реестр и PATH меняются редко — запоминаем результат ненадолго.
        var now = Stopwatch.GetTimestamp();
        if (ResolvedNames.TryGetValue(entry, out var cached) && Stopwatch.GetElapsedTime(cached.Timestamp) < ResolveCacheLifetime)
            return cached.Path;

        var resolved = FindPath(entry);
        ResolvedNames[entry] = (now, resolved);
        return resolved;
    }

    private static readonly TimeSpan ResolveCacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, (long Timestamp, string? Path)> ResolvedNames = new(StringComparer.OrdinalIgnoreCase);

    private static string? FindPath(string entry)
    {
        var file = entry + ".exe";
        try
        {
            foreach (var (hive, subKey) in AppPathsKeys)
            {
                using var key = hive.OpenSubKey($@"{subKey}\{file}");
                if (key?.GetValue(null) is string registered)
                {
                    var path = registered.Trim().Trim('"');
                    if (File.Exists(path))
                        return path;
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            // Реестр недоступен — пробуем PATH.
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), file);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Некорректный элемент PATH.
            }
        }

        return null;
    }

    /// <summary>
    /// Известные VPN-клиенты (<see cref="VpnClients"/>) GeoGuard не трогает никогда: сбой самого VPN — ровно то, от чего приложение защищает.
    /// Проверяется и запись списка (имя или путь), и конкретный процесс.
    /// </summary>
    public static bool IsProtected(string entry) =>
        IsPath(entry) ? IsProtectedPath(entry) || IsProtectedName(ProcessName(entry)) : IsProtectedName(entry);

    public static bool IsProtectedName(string processName) => VpnClients.IsClientName(processName);

    public static bool IsProtectedPath(string path) => VpnClients.IsClientPath(path);

    private static readonly (RegistryKey Hive, string SubKey)[] AppPathsKeys =
    [
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\App Paths"),
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"),
    ];
}
