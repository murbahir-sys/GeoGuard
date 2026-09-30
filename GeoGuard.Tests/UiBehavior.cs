using GeoGuard;

// Поведение окна настроек: «Применить», «Сохранить», «Отмена» и подписи про текущую страну.
internal static partial class Program
{
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("Сбой в потоке интерфейса: " + failure.Message, failure);
    }

    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;
            foreach (var nested in FindAll<T>(child))
                yield return nested;
        }
    }

    private static void ShowOffscreen(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-5000, 0);
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();
    }

    private static void SettingsFormApplyBehavior()
    {
        Console.WriteLine("# Окно настроек: «Применить» не закрывает окно; «Сохранить» и «Отмена»");
        var status = new GuardStatus(GuardState.Allowed, new GeoResult("DE", "Germany", "203.0.113.1"), DateTime.Now, null, false);
        AppSettings NewSettings() => new AppSettings
        {
            AllowedCountry = "DE",
            VpnAdapterMatch = "Amnezia",
            RestrictedApps = { "chrome" },
            CheckIntervalSeconds = 30,
        }.Normalize();

        RunSta(() =>
        {
            Application.EnableVisualStyles();
            var saved = new List<AppSettings>();
            var fail = false;
            var form = new SettingsForm(NewSettings(), status)
            {
                SaveHandler = s =>
                {
                    if (fail)
                        return null;
                    saved.Add(s);
                    return s.Normalize();
                },
            };
            ShowOffscreen(form);

            var apply = FindAll<Button>(form).Single(b => b.Text == "Применить");
            var save = FindAll<Button>(form).Single(b => b.Text == "Сохранить");
            var interval = FindAll<NumericUpDown>(form).Single();

            Check("«Применить» недоступна, пока ничего не изменено", !apply.Enabled);

            interval.Value = 45;
            Check("после изменения «Применить» становится доступной", apply.Enabled);

            apply.PerformClick();
            Application.DoEvents();
            Check("«Применить» передала новые настройки (интервал 45)", saved.Count == 1 && saved[0].CheckIntervalSeconds == 45);
            Check("окно настроек осталось открытым", !form.IsDisposed && form.Visible);
            Check("после применения «Применить» снова недоступна", !apply.Enabled);

            // Ошибка сохранения: окно остаётся, изменения не теряются.
            fail = true;
            interval.Value = 50;
            apply.PerformClick();
            Application.DoEvents();
            Check("при ошибке сохранения окно открыто, а «Применить» остаётся доступной", !form.IsDisposed && form.Visible && apply.Enabled && saved.Count == 1);

            fail = false;
            save.PerformClick();
            Application.DoEvents();
            Check("«Сохранить» применяет настройки (интервал 50)", saved.Count == 2 && saved[1].CheckIntervalSeconds == 50);
            Check("«Сохранить» закрывает окно", form.IsDisposed);
        });

        RunSta(() =>
        {
            Application.EnableVisualStyles();
            var calls = 0;
            var form = new SettingsForm(NewSettings(), status) { SaveHandler = s => { calls++; return s.Normalize(); } };
            ShowOffscreen(form);

            var country = FindAll<ComboBox>(form).Single(c => c.DropDownStyle == ComboBoxStyle.DropDownList);
            var us = country.Items.Cast<object>().First(i => i.ToString()!.Contains("(US)"));
            country.SelectedItem = us;
            FindAll<Button>(form).Single(b => b.Text == "Применить").PerformClick();
            Application.DoEvents();
            Check("выбор другой страны доходит до обработчика (US)", calls == 1 && ((string)country.SelectedItem!.ToString()!).Contains("(US)"));

            FindAll<NumericUpDown>(form).Single().Value = 99;
            FindAll<Button>(form).Single(b => b.Text == "Отмена").PerformClick();
            Application.DoEvents();
            Check("«Отмена» закрывает окно без вопросов и без сохранения", form.IsDisposed && calls == 1);
        });

        RunSta(() =>
        {
            Application.EnableVisualStyles();
            var form = new SettingsForm(NewSettings(), status)
            {
                // Обработчик вернул «нормализованный» вариант — окно должно показать именно его.
                SaveHandler = s => { var copy = s.Clone(); copy.VpnAdapterMatch = "Amnezia;WireGuard"; return copy; },
            };
            ShowOffscreen(form);
            var vpn = FindAll<ComboBox>(form).Single(c => c.DropDownStyle == ComboBoxStyle.DropDown);
            vpn.Text = "  Amnezia  ";
            var apply = FindAll<Button>(form).Single(b => b.Text == "Применить");
            apply.PerformClick();
            Application.DoEvents();
            Check("после применения в окне видно то, что реально сохранено", vpn.Text == "Amnezia;WireGuard" && !apply.Enabled);
            form.CloseWithoutPrompt();
        });

        Console.WriteLine("# Подписи про текущую страну по IP");
        Check("шапка: «Текущая страна по IP: …»", StatusView.MainLine(status).StartsWith("Текущая страна по IP: "));
        Check("шапка, страна не определена", StatusView.MainLine(new GuardStatus(GuardState.Unknown, null, null, null, true)).StartsWith("Текущая страна по IP: "));
        Check("строка состояния начинается с «Текущая страна по IP»", status.Describe().StartsWith("Текущая страна по IP: "));
        Check("строка состояния без страны тоже", new GuardStatus(GuardState.Unknown, null, null, null, true).Describe().StartsWith("Текущая страна по IP"));
    }

    private static void CountryFlagsWork()
    {
        Console.WriteLine("# Флаги стран");
        var assembly = typeof(AppInfo).Assembly;
        var resources = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Flags.", StringComparison.Ordinal)).ToList();
        Check($"флаги встроены в программу ({resources.Count} шт.)", resources.Count >= 250);

        var codes = System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures)
            .Select(c => { try { return new System.Globalization.RegionInfo(c.Name).TwoLetterISORegionName.ToUpperInvariant(); } catch (ArgumentException) { return ""; } })
            .Where(c => c.Length == 2 && c.All(char.IsAsciiLetter))
            .Distinct()
            .ToList();
        var missing = codes.Where(c => !CountryFlags.Has(c)).ToList();
        Check($"у всех {codes.Count} стран из списка выбора есть флаг", missing.Count == 0);
        if (missing.Count > 0)
            Console.WriteLine("    нет флага: " + string.Join(", ", missing));

        Check("регистр кода не важен", CountryFlags.Has("de") && CountryFlags.Has("DE"));
        Check("несуществующие и некорректные коды флага не имеют", !CountryFlags.Has("ZZ") && !CountryFlags.Has("") && !CountryFlags.Has(null) && !CountryFlags.Has("D") && !CountryFlags.Has("../"));

        using (var de = CountryFlags.Render("DE", 100, 80))
        {
            // Германия 5:3 вписывается в 100x80 как 100x60 по центру (строки 10..70): три равные полосы по 20 строк.
            var top = de.GetPixel(50, 18);
            var middle = de.GetPixel(50, 40);
            var bottom = de.GetPixel(50, 62);
            Check("флаг Германии нарисован верно: чёрная, красная и золотая полосы",
                top.R < 60 && top.G < 60 && top.B < 60 && top.A == 255
                && middle.R > 180 && middle.G < 60 && middle.B < 60
                && bottom.R > 200 && bottom.G > 150 && bottom.B < 80);
            Check("пропорции сохраняются: вне вписанного флага (сверху и снизу) прозрачно",
                de.GetPixel(50, 3).A == 0 && de.GetPixel(50, 77).A == 0);
            Check("углы скруглены (угловой пиксель флага прозрачный)", de.GetPixel(0, 10).A < 255);
        }

        using (var ch = CountryFlags.Render("CH", 40, 30))
        {
            Check("у Швейцарии квадратный флаг: по бокам поля прозрачны, в центре красный",
                ch.GetPixel(2, 15).A == 0 && ch.GetPixel(20, 4).R > 180 && ch.GetPixel(20, 4).G < 60);
        }

        Check("пропорции флагов настоящие (США шире Германии, Швейцария 1:1)",
            CountryFlags.AspectRatio("US") > CountryFlags.AspectRatio("DE") && Math.Abs(CountryFlags.AspectRatio("CH") - 1.0) < 0.01);

        using (var none = CountryFlags.Render(null, 40, 30))
            Check("для неизвестной страны рисуется заглушка, а не исключение", none.GetPixel(20, 15).A > 0);

        RunSta(() =>
        {
            Application.EnableVisualStyles();
            var form = new SettingsForm(new AppSettings { AllowedCountry = "DE" }.Normalize(),
                new GuardStatus(GuardState.Allowed, new GeoResult("DE", "Germany", "203.0.113.1"), DateTime.Now, null, false));
            var country = FindAll<ComboBox>(form).Single(c => c.DropDownStyle == ComboBoxStyle.DropDownList);
            Check("список стран рисуется с флагами (owner-draw)", country.DrawMode == DrawMode.OwnerDrawFixed);
            form.Dispose();
        });
    }

    /// <summary>Вкладки настроек и «О программе» помещаются без прокрутки и с запасом на перенос строк (другие шрифты, длинные тексты).</summary>
    private static void TabsFitWithoutScrolling()
    {
        Console.WriteLine("# Вкладки помещаются в окно без вертикальной прокрутки");
        const int reserve = 28; // примерно две лишние строки поясняющего текста

        RunSta(() =>
        {
            Application.EnableVisualStyles();
            var settings = new AppSettings
            {
                AllowedCountry = "DE",
                VpnAdapterMatch = "WireGuard",
                RestrictedApps = { @"C:\Program Files (x86)\Total Commander\TOTALCMD64.EXE", "chrome", "Telegram", "firefox" },
            }.Normalize();
            var status = new GuardStatus(GuardState.Unknown, null, null, "Не удалось определить страну по IP", true,
                "Файрвол выключен: нет прав администратора. Работает только закрытие приложений.");
            using var form = new SettingsForm(settings, status);
            ShowOffscreen(form);

            var pages = FindAll<FlowLayoutPanel>(form).Where(p => p.AutoScroll && p.Dock == DockStyle.Fill).ToList();
            string[] names = ["Защита", "Сеть", "Общие"];
            for (var i = 0; i < pages.Count && i < names.Length; i++)
            {
                form.SelectTab(i);
                Application.DoEvents();
                var page = pages[i];
                var last = page.Controls.Cast<Control>().Last();
                var slack = page.ClientSize.Height - (last.Bottom + last.Margin.Bottom);
                Console.WriteLine($"    «{names[i]}»: свободно снизу {slack} px");
                Check($"«{names[i]}»: без прокрутки и с запасом ≥ {reserve} px", !page.VerticalScroll.Visible && slack >= reserve);
            }

            form.CloseWithoutPrompt();
        });

        RunSta(() =>
        {
            Application.EnableVisualStyles();
            using var about = new AboutForm();
            ShowOffscreen(about);
            var body = FindAll<FlowLayoutPanel>(about).First(p => p.AutoScroll && p.Dock == DockStyle.Fill);
            var last = body.Controls.Cast<Control>().Last();
            var slack = body.ClientSize.Height - (last.Bottom + last.Margin.Bottom);
            Console.WriteLine($"    «О программе»: свободно снизу {slack} px");
            Check($"«О программе»: без прокрутки и с запасом ≥ {reserve} px", !body.VerticalScroll.Visible && slack >= reserve);
            about.Close();
        });
    }

    private static void StatusPillTexts()
    {
        Console.WriteLine("# Плашка статуса в шапке");
        var de = new GeoResult("DE", "Germany", "203.0.113.1");
        Check("разрешено: «Выбранным приложениям разрешён запуск»",
            StatusView.Pill(new GuardStatus(GuardState.Allowed, de, DateTime.Now, null, false)) == ("Выбранным приложениям разрешён запуск", Theme.Success));
        Check("страна не совпала: «Выбранные приложения заблокированы»",
            StatusView.Pill(new GuardStatus(GuardState.Blocking, de, DateTime.Now, null, true)) == ("Выбранные приложения заблокированы", Theme.Danger));
        Check("страна не подтверждена, приложения закрыты: сказано, что идёт проверка",
            StatusView.Pill(new GuardStatus(GuardState.Unknown, null, null, null, true)).Pill.Contains("заблокированы") && StatusView.Pill(new GuardStatus(GuardState.Unknown, null, null, null, true)).Pill.Contains("проверка"));
        Check("ожидание первого ответа при запуске: «Определяем страну…»",
            StatusView.Pill(new GuardStatus(GuardState.Unknown, null, null, null, false)).Pill == "Определяем страну…");
    }
}
