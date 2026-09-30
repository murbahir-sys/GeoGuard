using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace GeoGuard;

/// <summary>
/// Автозапуск при входе в Windows.
/// С правами администратора — задача планировщика с наивысшими правами (значение в реестре Run повышенные программы не запускает).
/// Без прав — обычная запись HKCU\...\Run (тогда при запуске будет запрос UAC).
/// </summary>
public static class AutoStart
{
    public const string TaskName = "GeoGuard";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GeoGuard";
    private static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <returns>false, если автозапуск не удалось изменить.</returns>
    public static bool Apply(bool enable)
    {
        if (!Elevation.IsElevated)
        {
            // Без прав задачу планировщика не создать и не удалить; уже существующая задача остаётся в силе.
            return enable ? TaskExists() || SetRunKey(true) : SetRunKey(false) && !TaskExists();
        }

        if (enable)
        {
            if (CreateTask())
                return SetRunKey(false); // задача запускает с нужными правами — запись Run не нужна

            return SetRunKey(true); // запасной вариант
        }

        return DeleteTask() & SetRunKey(false);
    }

    /// <summary>Убирает и задачу, и запись Run (удаление программы).</summary>
    public static bool Remove() => (!TaskExists() || DeleteTask()) & SetRunKey(false);

    /// <summary>XML задачи планировщика: запуск при входе указанного пользователя с наивысшими правами.</summary>
    public static XDocument BuildTaskXml(string exePath, string userName)
    {
        XElement E(string name, params object[] content) => new(TaskNs + name, content);

        return new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            E("Task",
                new XAttribute("version", "1.2"),
                E("RegistrationInfo", E("Description", "GeoGuard: запуск при входе в Windows с правами администратора")),
                E("Triggers", E("LogonTrigger", E("Enabled", "true"), E("UserId", userName))),
                E("Principals",
                    E("Principal",
                        new XAttribute("id", "Author"),
                        E("UserId", userName),
                        E("LogonType", "InteractiveToken"),
                        E("RunLevel", "HighestAvailable"))),
                E("Settings",
                    E("MultipleInstancesPolicy", "IgnoreNew"),
                    E("DisallowStartIfOnBatteries", "false"),
                    E("StopIfGoingOnBatteries", "false"),
                    E("AllowHardTerminate", "true"),
                    E("StartWhenAvailable", "true"),
                    E("RunOnlyIfNetworkAvailable", "false"),
                    E("AllowStartOnDemand", "true"),
                    E("Enabled", "true"),
                    E("Hidden", "false"),
                    E("ExecutionTimeLimit", "PT0S"),
                    E("Priority", "7")),
                E("Actions",
                    new XAttribute("Context", "Author"),
                    E("Exec",
                        E("Command", $"\"{exePath}\""),
                        E("WorkingDirectory", Path.GetDirectoryName(exePath) ?? "")))));
    }

    private static bool CreateTask()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;

        var xmlPath = Path.Combine(Path.GetTempPath(), $"geoguard-task-{Guid.NewGuid():N}.xml");
        try
        {
            using (var writer = XmlWriter.Create(xmlPath, new XmlWriterSettings { Encoding = Encoding.Unicode, Indent = true }))
                BuildTaskXml(exe, WindowsIdentity.GetCurrent().Name).Save(writer);

            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static bool DeleteTask() => !TaskExists() || RunSchtasks($"/Delete /TN \"{TaskName}\" /F");

    private static bool TaskExists() => RunSchtasks($"/Query /TN \"{TaskName}\"");

    private static bool RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
                return false;

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(15000) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool SetRunKey(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                    return false;
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }
}
