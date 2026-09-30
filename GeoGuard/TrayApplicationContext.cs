namespace GeoGuard;

/// <summary>Приложение живёт только в трее: главного окна нет, настройки открываются по требованию.</summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan NoticeInterval = TimeSpan.FromSeconds(10);

    private readonly Control _ui = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly GuardEngine _engine;
    private readonly FirewallSync _firewall;
    private readonly RegisteredWaitHandle _showWait;

    private AppSettings _settings;
    private SettingsForm? _form;
    private HelpForm? _help;
    private AboutForm? _about;
    private GuardState _lastState = GuardState.Unknown;
    private DateTime _lastBlockNotice = DateTime.MinValue;

    public TrayApplicationContext(EventWaitHandle showSettingsEvent)
    {
        var firstRun = !SettingsStore.Exists;
        _settings = SettingsStore.Load();
        AutoStart.Apply(_settings.StartWithWindows);

        _ = _ui.Handle; // создаём дескриптор заранее, чтобы BeginInvoke работал из фоновых потоков
        _firewall = new FirewallSync(new WindowsFirewall());
        _engine = new GuardEngine(_settings, firewall: _firewall);

        _statusItem = new ToolStripMenuItem("Страна ещё не определена") { Enabled = false };
        var menu = new ContextMenuStrip { Font = Theme.Base, ShowImageMargin = true, ImageScalingSize = new Size(Theme.Px(20), Theme.Px(15)) };
        menu.Items.Add(new ToolStripMenuItem($"{AppInfo.Name} {AppInfo.VersionText}") { Enabled = false, Font = Theme.Bold });
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Настройки…", null, (_, _) => ShowSettings());
        menu.Items.Add("Проверить сейчас", null, (_, _) => _engine.TriggerCheck());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Справка", null, (_, _) => ShowHelp());
        menu.Items.Add("О программе", null, (_, _) => ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Exit());

        _tray = new NotifyIcon
        {
            Icon = TrayIcons.Unknown,
            Text = AppInfo.Name,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _engine.StatusChanged += status => Post(() => OnStatusChanged(status));
        _engine.AppBlocked += name => Post(() => OnAppBlocked(name));
        _engine.Start();

        // Повторный запуск exe не создаёт второй экземпляр, а просит первый открыть настройки.
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            showSettingsEvent, (_, _) => Post(ShowSettings), null, Timeout.Infinite, executeOnlyOnce: false);

        if (firstRun)
            ShowSettings();
    }

    private void Post(Action action)
    {
        try
        {
            if (!_ui.IsDisposed)
                _ui.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Приложение уже закрывается.
        }
    }

    private void OnStatusChanged(GuardStatus status)
    {
        var description = status.Describe();
        _statusItem.Text = description.Split('\n')[0];
        _tray.Icon = TrayIcons.For(status.State);
        _tray.Text = Truncate(status.State switch
        {
            GuardState.Blocking => $"GeoGuard: {status.Geo?.CountryCode} — приложения заблокированы",
            GuardState.Allowed => $"GeoGuard: {status.Geo?.CountryCode} — приложения разрешены",
            _ => status.AppsBlocked ? "GeoGuard: страна не определена, приложения заблокированы" : "GeoGuard: страна не определена",
        }, 63);

        // Уведомляем только о реальной смене страны; Unknown при старте/потере сети молчит, чтобы не мигать при каждой загрузке.
        if (_settings.ShowNotifications)
        {
            if (status.State == GuardState.Blocking && _lastState != GuardState.Blocking)
                _tray.ShowBalloonTip(4000, "GeoGuard", $"Текущая страна по IP: {status.CountryLabel}. Приложения из списка закрываются — они разрешены только в другой стране.", ToolTipIcon.Warning);
            else if (status.State == GuardState.Allowed && _lastState == GuardState.Blocking)
                _tray.ShowBalloonTip(4000, "GeoGuard", $"Текущая страна по IP: {status.CountryLabel}. Приложения из списка снова разрешены.", ToolTipIcon.Info);
        }

        _lastState = status.State;
        _form?.UpdateStatus(status);
    }

    private void OnAppBlocked(string name)
    {
        // Заблокированное приложение могут запускать снова и снова — не заваливаем пользователя уведомлениями.
        var now = DateTime.UtcNow;
        if (!_settings.ShowNotifications || now - _lastBlockNotice < NoticeInterval)
            return;

        _lastBlockNotice = now;
        _tray.ShowBalloonTip(3000, "GeoGuard", $"Приложение «{name}» закрыто: разрешённая страна не подтверждена.", ToolTipIcon.Warning);
    }

    private void ShowSettings()
    {
        if (_form is { IsDisposed: false })
        {
            if (_form.WindowState == FormWindowState.Minimized)
                _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            return;
        }

        _form = new SettingsForm(_settings, _engine.Status);
        _form.SaveHandler = OnSettingsSaved;
        _form.CheckRequested += _engine.TriggerCheck;
        _form.OpenHelpRequested += ShowHelp;
        _form.OpenAboutRequested += ShowAbout;
        _form.FormClosed += (_, _) => _form = null;
        _form.Show();
        _form.Activate();
    }

    private void ShowHelp()
    {
        if (_help is { IsDisposed: false })
        {
            _help.Activate();
            return;
        }

        _help = new HelpForm();
        _help.FormClosed += (_, _) => _help = null;
        _help.Show();
        _help.Activate();
    }

    private void ShowAbout()
    {
        if (_about is { IsDisposed: false })
        {
            _about.Activate();
            return;
        }

        _about = new AboutForm();
        _about.OpenHelpRequested += ShowHelp;
        _about.FormClosed += (_, _) => _about = null;
        _about.Show();
        _about.Activate();
    }

    /// <returns>Сохранённые (нормализованные) настройки или null, если сохранить не удалось.</returns>
    private AppSettings? OnSettingsSaved(AppSettings settings)
    {
        settings.Normalize();

        try
        {
            SettingsStore.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Не удалось сохранить настройки:\n{ex.Message}", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }

        _settings = settings;
        if (!AutoStart.Apply(settings.StartWithWindows))
            MessageBox.Show("Не удалось изменить автозапуск (нет доступа к реестру или планировщику заданий).", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        _engine.ApplySettings(settings);
        return settings;
    }

    private void Exit()
    {
        var answer = MessageBox.Show(
            "Выход отключит защиту: приложения из списка перестанут закрываться, а правила файрвола будут удалены.\n\nВыйти?",
            "GeoGuard",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        _showWait.Unregister(null);
        _tray.Visible = false;
        _form?.CloseWithoutPrompt();
        _help?.Close();
        _about?.Close();
        _engine.Dispose();
        _firewall.Shutdown(); // только здесь правила удаляются: при выключении компьютера и сбое они остаются и продолжают защищать
        _tray.Dispose();
        _ui.Dispose();
        ExitThread();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
