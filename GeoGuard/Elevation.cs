using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace GeoGuard;

public static class Elevation
{
    public static bool IsElevated { get; } = ComputeIsElevated();

    /// <summary>Запускает эту же программу с запросом прав администратора (UAC).</summary>
    /// <returns>true — повышенная копия запущена, текущий процесс нужно завершить; false — пользователь отказал или запуск не удался.</returns>
    public static bool TryRelaunchElevated(string[] args)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
            })?.Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            return false; // отказ в окне UAC или ошибка запуска
        }
    }

    private static bool ComputeIsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
