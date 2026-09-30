using System.ComponentModel;
using System.Diagnostics;

namespace GeoGuard;

internal static class Shell
{
    /// <summary>
    /// Открывает папку, файл или ссылку через рабочий стол (explorer.exe), а не напрямую.
    /// Программа работает с правами администратора, и прямой запуск открыл бы браузер или проводник тоже с повышенными правами.
    /// </summary>
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = false })?.Dispose();
        }
        catch (Win32Exception)
        {
            // Проводник недоступен — открывать нечем.
        }
    }
}
