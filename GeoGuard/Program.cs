using System.Security.AccessControl;
using System.Security.Principal;

namespace GeoGuard;

internal static class Program
{
    private const string InstanceMutexName = @"Local\GeoGuard.SingleInstance";
    private const string ShowSettingsEventName = @"Local\GeoGuard.ShowSettings";

    /// <summary>
    /// Ключи: --no-elevate (не запрашивать права администратора; файрвол тогда недоступен),
    /// --remove-rules (удалить правила файрвола и автозапуск), --selftest-firewall (проверить файрвол на этой системе),
    /// --enable-autostart / --disable-autostart (включить или выключить автозапуск; для установщика),
    /// --silent (служебные команды без окон сообщений).
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var noElevate = args.Contains("--no-elevate");
        var removeRules = args.Contains("--remove-rules");
        var selfTest = args.Contains("--selftest-firewall");
        var enableAutostart = args.Contains("--enable-autostart");
        var disableAutostart = args.Contains("--disable-autostart");
        var silent = args.Contains("--silent");
        var maintenance = removeRules || selfTest || enableAutostart || disableAutostart;

        // Уже работает другая копия (возможно, повышенная): просим её показать настройки и выходим, не тревожа UAC.
        if (!maintenance && AnotherInstanceRunning())
        {
            SignalRunningInstance();
            return 0;
        }

        if (!Elevation.IsElevated && !noElevate)
        {
            if (Elevation.TryRelaunchElevated(args))
                return 0;

            if (maintenance)
            {
                if (!silent)
                    MessageBox.Show("Для этой команды нужны права администратора.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            // UAC отклонён: работаем в ограниченном режиме — приложения закрываются, файрвол недоступен.
        }

        if ((removeRules || selfTest) && AnotherInstanceRunning())
        {
            if (!silent)
            {
                MessageBox.Show(
                    "GeoGuard уже запущен. Закройте его через значок в трее («Выход») — при выходе правила файрвола удаляются автоматически.",
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return 1;
        }

        if (removeRules)
            return Maintenance.RemoveEverything(silent);
        if (selfTest)
            return Maintenance.FirewallSelfTest();
        if (enableAutostart || disableAutostart)
            return Maintenance.SetAutostart(enableAutostart);

        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalRunningInstance();
            return 0;
        }

        using var showSettingsEvent = CreateShowSettingsEvent();
        Application.Run(new TrayApplicationContext(showSettingsEvent));
        return 0;
    }

    private static bool AnotherInstanceRunning()
    {
        try
        {
            using var existing = Mutex.OpenExisting(InstanceMutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // мьютекс есть, но принадлежит повышенному процессу
        }
    }

    /// <summary>Событие доступно и обычному процессу (второй запуск), и повышенному (первый).</summary>
    private static EventWaitHandle CreateShowSettingsEvent()
    {
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(
            WindowsIdentity.GetCurrent().User!,
            EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize,
            AccessControlType.Allow));
        return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, ShowSettingsEventName, out _, security);
    }

    private static void SignalRunningInstance()
    {
        try
        {
            using var existing = EventWaitHandle.OpenExisting(ShowSettingsEventName);
            existing.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            // Первый экземпляр ещё запускается или недоступен — ничего страшного.
        }
    }
}
