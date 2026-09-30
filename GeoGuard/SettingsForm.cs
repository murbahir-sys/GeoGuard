using System.ComponentModel;
using System.Globalization;

namespace GeoGuard;

internal sealed class SettingsForm : Form
{
    // Отступы 24 слева и справа; справа их хватает и под полосу прокрутки, если она всё же понадобится (крупный шрифт).
    private const int CardWidth = 552;
    private const int Inner = CardWidth - 2 * Card.Pad;

    private sealed record CountryItem(string Code, string Name)
    {
        public override string ToString() => Code.Length == 0 ? Name : $"{Name} ({Code})";
    }

    /// <summary>Строка списка VPN-адаптеров; при выборе в поле подставляется имя адаптера.</summary>
    private sealed record AdapterItem(AdapterInfo Adapter)
    {
        public override string ToString() => Adapter.Name;
    }

    private readonly HeaderPanel _header = new(168);
    private readonly TabStrip _tabs = new("Защита", "Сеть", "Общие");
    private readonly List<Control> _pages = new();
    private readonly Panel _body = new();

    private readonly ComboBox _country = new();
    private readonly AppListBox _apps = new();
    private readonly TextBox _newApp = new();
    private readonly NumericUpDown _interval = new();
    private readonly ComboBox _vpnAdapter = new();
    private readonly CheckBox _useFirewall = new();
    private readonly CheckBox _autoStart = new();
    private readonly CheckBox _notifications = new();

    private FlatButton _applyButton = null!;
    private bool _dirty;
    private bool _loading;

    /// <summary>
    /// Сохраняет и применяет настройки. Возвращает сохранённые (нормализованные) настройки
    /// или null, если сохранить не удалось — тогда окно остаётся открытым, а изменения не теряются.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AppSettings, AppSettings?>? SaveHandler { get; set; }
    public event Action? CheckRequested;
    public event Action? OpenHelpRequested;
    public event Action? OpenAboutRequested;

    public SettingsForm(AppSettings settings, GuardStatus status)
    {
        Text = $"{AppInfo.Name} — настройки";
        Icon = AppAssets.Icon(32);
        Font = Theme.Base;
        BackColor = Theme.Background;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(600, 690);
        KeyPreview = true;

        _body.Dock = DockStyle.Fill;
        _body.BackColor = Theme.Background;
        _pages.Add(BuildProtectionPage(settings));
        _pages.Add(BuildNetworkPage(settings));
        _pages.Add(BuildGeneralPage(settings));
        foreach (var page in _pages)
            _body.Controls.Add(page);
        ShowPage(0);
        _tabs.SelectedChanged += () => ShowPage(_tabs.SelectedIndex);

        var footer = BuildFooter();

        // Порядок важен для Dock: сначала заполняющая область, затем нижняя и верхние панели.
        Controls.Add(_body);
        Controls.Add(footer);
        Controls.Add(_tabs);
        Controls.Add(_header);
        _header.Title = AppInfo.Name;
        _header.Subtitle = AppInfo.Tagline;

        UpdateStatus(status);

        // Изменения отслеживаем только после того, как выставлены начальные значения.
        _country.SelectedIndexChanged += (_, _) => MarkDirty();
        _interval.ValueChanged += (_, _) => MarkDirty();
        _vpnAdapter.TextChanged += (_, _) => MarkDirty();
        _useFirewall.CheckedChanged += (_, _) => MarkDirty();
        _autoStart.CheckedChanged += (_, _) => MarkDirty();
        _notifications.CheckedChanged += (_, _) => MarkDirty();
        SetDirty(false);
        FormClosing += OnFormClosing;
    }

    /// <summary>Переключает вкладку (для тестов отрисовки).</summary>
    internal void SelectTab(int index) => _tabs.SelectedIndex = index;

    public void UpdateStatus(GuardStatus status)
    {
        var (pill, color) = StatusView.Pill(status);
        _header.SetStatus(pill, color, StatusView.Prefix, StatusView.Value(status), status.Geo?.CountryCode, StatusView.Details(status));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F1)
        {
            e.Handled = true;
            OpenHelpRequested?.Invoke();
        }

        base.OnKeyDown(e);
    }

    // ---- страницы ----------------------------------------------------------------------------------------------

    private static FlowLayoutPanel NewPage() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(24, 12, 0, 0),
        BackColor = Theme.Background,
    };

    private void ShowPage(int index)
    {
        for (var i = 0; i < _pages.Count; i++)
            _pages[i].Visible = i == index;
    }

    private Control BuildProtectionPage(AppSettings settings)
    {
        var page = NewPage();

        var country = new Card(CardWidth);
        country.Flow.Controls.Add(Theme.Heading2("Разрешённая страна", Inner));
        country.Flow.Controls.Add(Theme.Hint("Приложения из списка работают только в этой стране, в остальных — закрываются.", Inner, 6));
        _country.DropDownStyle = ComboBoxStyle.DropDownList;
        _country.DrawMode = DrawMode.OwnerDrawFixed;
        _country.ItemHeight = Theme.Px(24);
        _country.DropDownHeight = Theme.Px(360);
        _country.DrawItem += DrawCountryItem;
        _country.Width = Inner;
        _country.Margin = Padding.Empty;
        var countries = BuildCountries(settings.AllowedCountry);
        _country.Items.AddRange(countries.Cast<object>().ToArray());
        _country.SelectedItem = countries.First(c => c.Code == settings.AllowedCountry);
        country.Flow.Controls.Add(_country);
        page.Controls.Add(country);

        var apps = new Card(CardWidth);
        apps.Flow.Controls.Add(Theme.Heading2("Приложения", Inner));
        apps.Flow.Controls.Add(Theme.Hint("Имя процесса из Диспетчера задач (без .exe) или путь к .exe через «Обзор…».", Inner, 6));

        var listHost = new Panel { Size = new Size(Inner, 3 * 40 + 2), BackColor = Theme.Border, Padding = new Padding(1), Margin = new Padding(0, 0, 0, 8) };
        _apps.Dock = DockStyle.Fill;
        _apps.Items.AddRange(settings.RestrictedApps.Cast<object>().ToArray());
        _apps.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
                RemoveSelectedApps();
        };
        listHost.Controls.Add(_apps);
        apps.Flow.Controls.Add(listHost);

        var addRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Surface };
        _newApp.Width = Inner - 300; // остальное место в строке занимают три кнопки с отступами
        _newApp.Font = Theme.Base;
        _newApp.BorderStyle = BorderStyle.FixedSingle;
        _newApp.Margin = new Padding(0, 4, 8, 0);
        _newApp.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            AddApp(_newApp.Text);
        };
        addRow.Controls.Add(_newApp);
        var add = new FlatButton("Добавить", ButtonKind.Primary, 92);
        add.Click += (_, _) => AddApp(_newApp.Text);
        addRow.Controls.Add(add);
        var browse = new FlatButton("Обзор…", ButtonKind.Secondary, 88);
        browse.Click += (_, _) => BrowseForApp();
        addRow.Controls.Add(browse);
        var remove = new FlatButton("Удалить", ButtonKind.Secondary, 96) { Margin = Padding.Empty };
        remove.Click += (_, _) => RemoveSelectedApps();
        addRow.Controls.Add(remove);
        apps.Flow.Controls.Add(addRow);
        page.Controls.Add(apps);
        return page;
    }

    private Control BuildNetworkPage(AppSettings settings)
    {
        var page = NewPage();

        var vpn = new Card(CardWidth);
        vpn.Flow.Controls.Add(Theme.Heading2("VPN и файрвол", Inner));
        vpn.Flow.Controls.Add(Theme.Hint("Приложениям разрешён выход в сеть только через адаптер вашего VPN.", Inner, 6));
        vpn.Flow.Controls.Add(Theme.Caption("VPN-адаптер", Inner));
        _vpnAdapter.DropDownStyle = ComboBoxStyle.DropDown;
        _vpnAdapter.DrawMode = DrawMode.OwnerDrawVariable;
        _vpnAdapter.MeasureItem += (_, e) => e.ItemHeight = Theme.Px(40);
        _vpnAdapter.DrawItem += DrawAdapterItem;
        _vpnAdapter.DropDownHeight = Theme.Px(300);
        _vpnAdapter.DropDown += (_, _) => FillAdapters();
        _vpnAdapter.Width = Inner;
        _vpnAdapter.Margin = new Padding(0, 0, 0, 4);
        _vpnAdapter.Text = settings.VpnAdapterMatch;
        FillAdapters();
        vpn.Flow.Controls.Add(_vpnAdapter);
        vpn.Flow.Controls.Add(Theme.Hint("Выберите адаптер из списка (похожие на VPN — первыми) или введите часть его имени; несколько — через «;».", Inner, 10));

        _useFirewall.Text = "Закрывать приложениям сеть вне VPN (файрвол)";
        _useFirewall.AutoSize = true;
        _useFirewall.Font = Theme.Base;
        _useFirewall.Checked = settings.UseFirewall;
        _useFirewall.Margin = new Padding(0, 0, 0, 2);
        vpn.Flow.Controls.Add(_useFirewall);
        vpn.Flow.Controls.Add(Theme.Hint(Elevation.IsElevated
            ? "Права администратора есть — файрвол доступен."
            : "Нужны права администратора: сейчас их нет, файрвол не работает.", Inner, 0));
        page.Controls.Add(vpn);

        var check = new Card(CardWidth);
        check.Flow.Controls.Add(Theme.Heading2("Проверка страны", Inner));
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 6), BackColor = Theme.Surface };
        row.Controls.Add(new Label { Text = "Проверять каждые", AutoSize = true, Font = Theme.Base, ForeColor = Theme.Text, Margin = new Padding(0, 5, 8, 0) });
        _interval.Width = 76;
        _interval.Minimum = AppSettings.MinIntervalSeconds;
        _interval.Maximum = AppSettings.MaxIntervalSeconds;
        _interval.Value = Math.Clamp(settings.CheckIntervalSeconds, AppSettings.MinIntervalSeconds, AppSettings.MaxIntervalSeconds);
        _interval.Margin = new Padding(0, 2, 8, 0);
        row.Controls.Add(_interval);
        row.Controls.Add(new Label { Text = "сек.", AutoSize = true, Font = Theme.Base, ForeColor = Theme.Text, Margin = new Padding(0, 5, 0, 0) });
        check.Flow.Controls.Add(row);
        check.Flow.Controls.Add(Theme.Hint("При смене сети страна проверяется сразу, независимо от интервала.", Inner, 0));
        page.Controls.Add(check);
        return page;
    }

    private Control BuildGeneralPage(AppSettings settings)
    {
        var page = NewPage();

        var startup = new Card(CardWidth);
        startup.Flow.Controls.Add(Theme.Heading2("Запуск", Inner));
        _autoStart.Text = "Запускать вместе с Windows";
        _autoStart.AutoSize = true;
        _autoStart.Font = Theme.Base;
        _autoStart.Checked = settings.StartWithWindows;
        _autoStart.Margin = new Padding(0, 0, 0, 2);
        startup.Flow.Controls.Add(_autoStart);
        startup.Flow.Controls.Add(Theme.Hint("Программа стартует при входе в систему и работает в трее.", Inner, 10));
        _notifications.Text = "Показывать уведомления";
        _notifications.AutoSize = true;
        _notifications.Font = Theme.Base;
        _notifications.Checked = settings.ShowNotifications;
        _notifications.Margin = new Padding(0, 0, 0, 2);
        startup.Flow.Controls.Add(_notifications);
        startup.Flow.Controls.Add(Theme.Hint("О смене страны и о закрытых приложениях.", Inner, 0));
        page.Controls.Add(startup);

        var actions = new Card(CardWidth);
        actions.Flow.Controls.Add(Theme.Heading2("Состояние и сведения", Inner));
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(Inner, 0), Width = Inner, Margin = Padding.Empty, BackColor = Theme.Surface };
        var checkNow = new FlatButton("Проверить сейчас", ButtonKind.Secondary, 150) { Margin = new Padding(0, 0, 8, 8) };
        checkNow.Click += (_, _) => CheckRequested?.Invoke();
        buttons.Controls.Add(checkNow);
        var help = new FlatButton("Справка (F1)", ButtonKind.Secondary, 130) { Margin = new Padding(0, 0, 8, 8) };
        help.Click += (_, _) => OpenHelpRequested?.Invoke();
        buttons.Controls.Add(help);
        var about = new FlatButton("О программе", ButtonKind.Secondary, 130) { Margin = new Padding(0, 0, 0, 8) };
        about.Click += (_, _) => OpenAboutRequested?.Invoke();
        buttons.Controls.Add(about);
        actions.Flow.Controls.Add(buttons);
        page.Controls.Add(actions);
        return page;
    }

    private Control BuildFooter()
    {
        var save = new FlatButton("Сохранить", ButtonKind.Primary, 120) { Margin = Padding.Empty };
        save.Click += (_, _) =>
        {
            if (!_dirty || Apply())
                Close();
        };
        _applyButton = new FlatButton("Применить", ButtonKind.Secondary, 112) { Margin = new Padding(0, 0, 8, 0) };
        _applyButton.Click += (_, _) => Apply();
        var cancel = new FlatButton("Отмена", ButtonKind.Secondary, 100) { Margin = new Padding(0, 0, 8, 0) };
        cancel.Click += (_, _) => CloseWithoutPrompt();
        CancelButton = cancel;
        return Ui.Footer(save, _applyButton, cancel);
    }

    /// <summary>Закрывает окно, отбрасывая несохранённые изменения без вопросов («Отмена», выход из программы).</summary>
    internal void CloseWithoutPrompt()
    {
        _dirty = false;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_dirty || e.CloseReason != CloseReason.UserClosing)
            return;

        var answer = MessageBox.Show(this, "Сохранить изменения?", AppInfo.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !Apply()))
            e.Cancel = true;
    }

    private void MarkDirty()
    {
        if (!_loading)
            SetDirty(true);
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        _applyButton.Enabled = dirty;
    }

    /// <summary>Заполняет список VPN-адаптеров: похожие на VPN первыми, затем подключённые. Введённый текст не меняется.</summary>
    private void FillAdapters()
    {
        IReadOnlyList<AdapterInfo> adapters;
        try
        {
            adapters = FirewallPlanner.SystemAdapters();
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
            return; // список недоступен — имя можно ввести вручную
        }

        var text = _vpnAdapter.Text;
        var wasLoading = _loading;
        _loading = true;
        try
        {
            _vpnAdapter.BeginUpdate();
            _vpnAdapter.Items.Clear();
            _vpnAdapter.Items.AddRange(adapters
                .Where(a => !a.IsLoopback)
                .DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(a => a.LooksLikeVpn)
                .ThenByDescending(a => a.IsUp)
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => (object)new AdapterItem(a))
                .ToArray());
            _vpnAdapter.EndUpdate();
            _vpnAdapter.Text = text;
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    /// <summary>Строка списка адаптеров: имя, пометки «похож на VPN» / «отключён» и описание мелко.</summary>
    private void DrawAdapterItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _vpnAdapter.Items.Count || _vpnAdapter.Items[e.Index] is not AdapterItem item)
            return;

        var g = e.Graphics;
        var bounds = e.Bounds;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.AccentSoft : Theme.Surface))
            g.FillRectangle(back, bounds);

        var px = Theme.Px;
        var x = bounds.X + px(8);
        var nameWidth = Math.Min(
            TextRenderer.MeasureText(g, item.Adapter.Name, Theme.Bold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width,
            bounds.Width - px(190));
        TextRenderer.DrawText(g, item.Adapter.Name, Theme.Bold, new Rectangle(x, bounds.Y + px(3), nameWidth + px(2), px(18)),
            Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        x += nameWidth + px(8);

        void Tag(string text, Color color)
        {
            var size = TextRenderer.MeasureText(g, text, Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
            var rect = new Rectangle(x, bounds.Y + px(4), size.Width + px(12), px(16));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(rect, px(8)))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
            TextRenderer.DrawText(g, text, Theme.Small, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x = rect.Right + px(6);
        }

        if (item.Adapter.LooksLikeVpn)
            Tag("похож на VPN", Theme.Accent);
        if (!item.Adapter.IsUp)
            Tag("отключён", Theme.Neutral);

        TextRenderer.DrawText(g, item.Adapter.Description, Theme.Small, new Rectangle(bounds.X + px(8), bounds.Y + px(21), bounds.Width - px(16), px(16)),
            Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    /// <summary>Строка списка стран: флаг и название. В поле (закрытом списке) фон обычный, в раскрытом — мягко подсвечивается выбранная строка.</summary>
    private void DrawCountryItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _country.Items.Count)
            return;

        var item = (CountryItem)_country.Items[e.Index]!;
        var highlighted = (e.State & DrawItemState.ComboBoxEdit) == 0 && (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(highlighted ? Theme.AccentSoft : Theme.Surface))
            e.Graphics.FillRectangle(back, e.Bounds);

        var flag = new Rectangle(e.Bounds.X + Theme.Px(6), e.Bounds.Y + (e.Bounds.Height - Theme.Px(15)) / 2, Theme.Px(20), Theme.Px(15));
        CountryFlags.Draw(e.Graphics, item.Code.Length == 0 ? null : item.Code, flag);

        var textLeft = flag.Right + Theme.Px(8);
        TextRenderer.DrawText(e.Graphics, item.ToString(), Theme.Base,
            new Rectangle(textLeft, e.Bounds.Y, Math.Max(0, e.Bounds.Right - textLeft - Theme.Px(4)), e.Bounds.Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    // ---- действия ----------------------------------------------------------------------------------------------

    private void AddApp(string input)
    {
        var entry = AppEntry.Normalize(input);
        if (input.Trim().Length > 0 && entry.Length == 0)
        {
            MessageBox.Show(
                "Не удалось разобрать запись. Введите имя процесса (например, chrome) или полный путь к .exe (например, C:\\Games\\game.exe).",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (entry.Length > 0 && AppEntry.IsProtected(entry))
        {
            MessageBox.Show(
                "Это VPN-клиент. GeoGuard никогда не закрывает и не блокирует VPN-программы, чтобы не отключить сам VPN.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (entry.Length > 0 && !_apps.Items.Cast<string>().Contains(entry, StringComparer.OrdinalIgnoreCase))
        {
            _apps.Items.Add(entry);
            MarkDirty();
        }

        _newApp.Clear();
        _newApp.Focus();
    }

    private void BrowseForApp()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите программу",
            Filter = "Программы (*.exe)|*.exe",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            AddApp(dialog.FileName);
    }

    private void RemoveSelectedApps()
    {
        var selected = _apps.SelectedItems.Cast<object>().ToList();
        foreach (var item in selected)
            _apps.Items.Remove(item);
        if (selected.Count > 0)
            MarkDirty();
    }

    private AppSettings BuildSettings() => new()
    {
        AllowedCountry = ((CountryItem)_country.SelectedItem!).Code,
        RestrictedApps = _apps.Items.Cast<string>().ToList(),
        CheckIntervalSeconds = (int)_interval.Value,
        StartWithWindows = _autoStart.Checked,
        ShowNotifications = _notifications.Checked,
        VpnAdapterMatch = _vpnAdapter.Text,
        UseFirewall = _useFirewall.Checked,
    };

    /// <summary>Сохраняет и применяет настройки, не закрывая окно. false — не удалось сохранить.</summary>
    private bool Apply()
    {
        var saved = SaveHandler is null ? BuildSettings().Normalize() : SaveHandler(BuildSettings());
        if (saved is null)
            return false;

        Reload(saved);
        SetDirty(false);
        return true;
    }

    /// <summary>Показывает в окне то, что реально сохранено (например, без отброшенных некорректных записей).</summary>
    private void Reload(AppSettings saved)
    {
        _loading = true;
        try
        {
            _apps.Items.Clear();
            _apps.Items.AddRange(saved.RestrictedApps.Cast<object>().ToArray());
            _vpnAdapter.Text = saved.VpnAdapterMatch;
            _interval.Value = Math.Clamp(saved.CheckIntervalSeconds, AppSettings.MinIntervalSeconds, AppSettings.MaxIntervalSeconds);
        }
        finally
        {
            _loading = false;
        }
    }

    private static List<CountryItem> BuildCountries(string selectedCode)
    {
        var byCode = new Dictionary<string, string>();
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);
                var code = region.TwoLetterISORegionName.ToUpperInvariant();
                if (code.Length == 2 && code.All(char.IsAsciiLetter))
                    byCode.TryAdd(code, CountryNames.For(code, region));
            }
            catch (ArgumentException)
            {
                // Культура без региона.
            }
        }

        // Код из файла настроек может отсутствовать в системном списке — показываем его как есть.
        if (selectedCode.Length > 0)
            byCode.TryAdd(selectedCode, CountryNames.For(selectedCode));

        var list = byCode
            .Select(kv => new CountryItem(kv.Key, kv.Value))
            .OrderBy(c => c.Name, StringComparer.CurrentCulture)
            .ToList();
        list.Insert(0, new CountryItem("", "— не выбрана (ограничений нет) —"));
        return list;
    }
}
