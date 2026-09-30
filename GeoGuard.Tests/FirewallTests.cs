using System.Diagnostics;
using GeoGuard;

// Тесты записей списка, защиты VPN-клиентов, правил файрвола, задачи автозапуска и COM-API файрвола.
internal static partial class Program
{
    private static void AppEntriesAndProtection()
    {
        Console.WriteLine("# Записи списка: имя или путь; VPN-клиенты защищены (проверяются только строки, реальные процессы не затрагиваются)");
        Check("имя без .exe остаётся именем", AppEntry.Normalize("chrome") == "chrome");
        Check("«Notepad.EXE» → «Notepad»", AppEntry.Normalize("  \"Notepad.EXE\" ") == "Notepad");
        Check("полный путь сохраняется вместе с .exe", AppEntry.Normalize(@"C:\Games\Game\game.exe") == @"C:\Games\Game\game.exe");
        Check("относительный путь отвергается", AppEntry.Normalize(@"sub\app.exe") == "" && AppEntry.Normalize("C:app.exe") == "");
        Check("пустая запись отвергается", AppEntry.Normalize("   ") == "");
        Check("имя процесса из пути", AppEntry.ProcessName(@"C:\Games\Game\game.exe") == "game" && AppEntry.ProcessName("chrome") == "chrome");
        Check("путь-запись разрешается в себя", AppEntry.ResolvePath(@"C:\Games\Game\game.exe") == @"C:\Games\Game\game.exe");
        Check("имя, которого нет в системе, не разрешается", AppEntry.ResolvePath("geoguard_no_such_app_zz") is null);

        Check("защищены имена Amnezia", AppEntry.IsProtected("AmneziaVPN") && AppEntry.IsProtected("amneziavpn-service") && AppEntry.IsProtected("AmneziaWG"));
        Check("защищён путь в папке AmneziaVPN", AppEntry.IsProtected(@"C:\Program Files\AmneziaVPN\openvpn.exe"));
        Check("обычные приложения не защищены", !AppEntry.IsProtected("chrome") && !AppEntry.IsProtected(@"C:\Games\game.exe"));
        Check("защищены и другие VPN-клиенты по имени (WireGuard, OpenVPN, Proton, NordVPN, Mullvad, Outline, v2rayN)",
            new[] { "wireguard", "openvpn-gui", "OpenVPNConnect", "ProtonVPN", "nordvpn-service", "mullvad-daemon", "Outline", "v2rayN" }.All(AppEntry.IsProtected));
        Check("защищены и по папке установки",
            AppEntry.IsProtected(@"C:\Program Files\WireGuard\wg.exe") && AppEntry.IsProtected(@"C:\Program Files\OpenVPN\bin\openvpn.exe")
            && AppEntry.IsProtected(@"C:\Program Files\Mullvad VPN\resources\mullvad-daemon.exe"));
        Check("похожие по имени обычные программы не защищены (openvpnlike, torrent, telegram)",
            !AppEntry.IsProtected("openvpnlike") && !AppEntry.IsProtected("qbittorrent") && !AppEntry.IsProtected("Telegram"));
        Check("по умолчанию VPN-адаптер не задан (без привязки к конкретному VPN)", new AppSettings().VpnAdapterMatch == "");

        var settings = new AppSettings
        {
            AllowedCountry = " de ",
            RestrictedApps = new List<string> { "chrome", "CHROME.exe", "AmneziaVPN", @"C:\Program Files\AmneziaVPN\AmneziaVPN.exe", @"C:\Games\game.exe", "sub\\x.exe", " " },
        }.Normalize();
        Check("Normalize: страна приводится к DE", settings.AllowedCountry == "DE");
        Check("Normalize: остаются chrome и game.exe, Amnezia и мусор выброшены, дубли схлопнуты",
            settings.RestrictedApps.Count == 2 && settings.RestrictedApps.Contains("chrome") && settings.RestrictedApps.Contains(@"C:\Games\game.exe"));
        Check("имена процессов для закрытия: chrome и game", settings.ProcessNames().OrderBy(n => n).SequenceEqual(new[] { "chrome", "game" }));
    }

    /// <summary>Адаптер для тестов. gateway — есть ли шлюз (выход в интернет), wireless — Wi-Fi.</summary>
    private static AdapterInfo Adapter(string name, string description = "", bool up = true, bool gateway = false, bool wireless = false, bool loopback = false, string ip = "10.0.0.1") =>
        new("id-" + name, name, description, loopback, up, wireless, new[] { ip }, gateway ? new[] { "10.0.0.254" } : Array.Empty<string>());

    private static readonly AdapterInfo Uplink = Adapter("vEthernet (Виртуальный коммутатор)", "Hyper-V Virtual Ethernet Adapter #3", gateway: true);
    private static readonly AdapterInfo VpnAdapter = Adapter("AmneziaVPN", "WireGuard Tunnel", ip: "10.8.1.1");
    private static readonly AdapterInfo Wsl = Adapter("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter #2", ip: "172.20.0.1");
    private static readonly AdapterInfo DefaultSwitch = Adapter("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter", ip: "172.28.0.1");
    private static readonly AdapterInfo VmNet = Adapter("VMware Network Adapter VMnet8", "VMware Virtual Ethernet Adapter for VMnet8", ip: "192.168.5.1");
    private static readonly AdapterInfo DisconnectedNic = Adapter("Ethernet", "Realtek PCIe GbE Family Controller", up: false);
    private static readonly AdapterInfo Loopback = Adapter("Loopback Pseudo-Interface 1", "Software Loopback Interface 1", loopback: true, ip: "127.0.0.1");

    private static readonly IReadOnlyList<AdapterInfo> SampleAdapters = new[] { DisconnectedNic, Uplink, Wsl, DefaultSwitch, VmNet, VpnAdapter, Loopback };

    private static bool SameSet(IEnumerable<string> actual, params string[] expected) =>
        actual.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expected);

    private static AppSettings FirewallSettings(params string[] apps) => new()
    {
        AllowedCountry = "DE",
        VpnAdapterMatch = "Amnezia",
        UseFirewall = true,
        RestrictedApps = apps.ToList(),
        CheckIntervalSeconds = 1,
    };

    private static AppSettings FirewallSettingsWith(Action<AppSettings> change)
    {
        var settings = FirewallSettings(@"C:\Fake\geoguard_fake_zz.exe");
        change(settings);
        return settings;
    }

    private static void FirewallPlannerRules()
    {
        Console.WriteLine("# Правила файрвола: что и где блокируется");
        const string app = @"C:\Fake\geoguard_fake_zz.exe";

        var allowed = FirewallPlanner.Plan(FirewallSettings(app), appsBlocked: false, SampleAdapters);
        Check("страна подтверждена: одно правило", allowed.Rules.Count == 1 && allowed.Rules[0].Path == app);
        Check("закрыты только интернет-интерфейсы: выключенный кабель и реальный выход; без VPN, loopback, WSL, Default Switch и VMware",
            allowed.Rules[0].Interfaces is { } i && i.SequenceEqual(new[] { "Ethernet", "vEthernet (Виртуальный коммутатор)" }));

        var blocked = FirewallPlanner.Plan(FirewallSettings(app), appsBlocked: true, SampleAdapters);
        Check("страна не подтверждена: блокировка на ВСЕХ интерфейсах (Interfaces = null)", blocked.Rules.Count == 1 && blocked.Rules[0].Interfaces is null);

        var vpnOnly = FirewallPlanner.Plan(FirewallSettings(app), false, new[] { VpnAdapter, Wsl, DefaultSwitch, Loopback });
        Check("вне VPN закрывать нечего (остались VPN, loopback и внутренние сети) — правил нет, а не «закрыть всё»", vpnOnly.Rules.Count == 0);

        var noVpnPresent = FirewallPlanner.Plan(FirewallSettings(app), false, new[] { Uplink, Wsl, Loopback });
        Check("VPN выключен (адаптера нет): реальный выход закрыт, внутренняя сеть WSL — нет",
            noVpnPresent.Rules.Count == 1 && noVpnPresent.Rules[0].Interfaces is { } j && j.SequenceEqual(new[] { "vEthernet (Виртуальный коммутатор)" }));

        var wifi = Adapter("Wi-Fi", "Intel Wireless", up: true, wireless: true);
        var withWifi = FirewallPlanner.Plan(FirewallSettings(app), false, new[] { wifi, VpnAdapter });
        Check("Wi-Fi закрывается даже до получения шлюза (иначе окно утечки при подключении)",
            withWifi.Rules.Count == 1 && withWifi.Rules[0].Interfaces is { } w && w.SequenceEqual(new[] { "Wi-Fi" }));

        var wslStopped = FirewallPlanner.Plan(FirewallSettings(app), false, new[] { Uplink, Adapter("vEthernet (WSL)", up: false), VpnAdapter });
        Check("остановленный (Down) внутренний адаптер попадает в правило — безвредно и без окна, если он вдруг станет выходом",
            SameSet(wslStopped.Rules[0].Interfaces!, "vEthernet (Виртуальный коммутатор)", "vEthernet (WSL)"));

        Check("страна не выбрана — правил нет", FirewallPlanner.Plan(FirewallSettingsWith(s => s.AllowedCountry = ""), false, SampleAdapters).Rules.Count == 0);
        Check("файрвол выключен галочкой — правил нет", FirewallPlanner.Plan(FirewallSettingsWith(s => s.UseFirewall = false), false, SampleAdapters).Rules.Count == 0);
        Check("VPN-адаптер не задан — правил нет", FirewallPlanner.Plan(FirewallSettingsWith(s => s.VpnAdapterMatch = ""), false, SampleAdapters).Rules.Count == 0);

        var mixed = FirewallPlanner.Plan(FirewallSettings(app, app.ToUpperInvariant(), @"C:\Program Files\AmneziaVPN\AmneziaVPN.exe", "geoguard_no_such_app_zz"), false, SampleAdapters);
        Check("дубли по регистру схлопнуты, защищённый путь исключён (правило только одно)", mixed.Rules.Count == 1);
        Check("запись без найденного пути перечислена как «без файрвола»", mixed.Unresolved.SequenceEqual(new[] { "geoguard_no_such_app_zz" }));
        Check("совпадение VPN-адаптера по описанию тоже работает", FirewallPlanner.IsVpnAdapter(Adapter("Подключение 5", "AmneziaWG Tunnel"), "amnezia"));
        Check("несколько шаблонов VPN через «;» и «,»",
            VpnMatch.IsMatch(Adapter("WireGuard Tunnel"), "Amnezia; WireGuard") && VpnMatch.IsMatch(Adapter("OpenVPN"), "x,openvpn") && !VpnMatch.IsMatch(Adapter("Ethernet"), "Amnezia;WireGuard"));
    }

    private static void VpnAdapterDetection()
    {
        Console.WriteLine("# Распознавание адаптеров, похожих на VPN (подсказка в списке выбора)");
        AdapterInfo Typed(string name, string description, bool tunnel = false) =>
            new("id-" + name, name, description, false, true, false, ["10.0.0.1"], [], tunnel);

        Check("по драйверу: WireGuard, Wintun, TAP, OpenVPN, NordLynx",
            Typed("wg0", "WireGuard Tunnel").LooksLikeVpn && Typed("Ethernet 5", "Wintun Userspace Tunnel").LooksLikeVpn
            && Typed("Ethernet 3", "TAP-Windows Adapter V9").LooksLikeVpn && Typed("OpenVPN Data Channel Offload", "OpenVPN DCO").LooksLikeVpn
            && Typed("NordLynx", "NordLynx Tunnel").LooksLikeVpn);
        Check("по типу: туннель/PPP/виртуальный адаптер с незнакомым именем", Typed("Мой офис", "Some Corp Adapter", tunnel: true).LooksLikeVpn);
        Check("по имени подключения: «Рабочий VPN»", Typed("Рабочий VPN", "WAN Miniport (IKEv2)").LooksLikeVpn);
        Check("обычные сетевые карты, Wi-Fi, WSL, VMware — не VPN",
            !Typed("Ethernet", "Realtek PCIe GbE Family Controller").LooksLikeVpn && !Typed("Wi-Fi", "Intel(R) Wi-Fi 6 AX201").LooksLikeVpn
            && !Typed("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter #2").LooksLikeVpn
            && !Typed("VMware Network Adapter VMnet8", "VMware Virtual Ethernet Adapter for VMnet8").LooksLikeVpn);
        Check("служебные туннели Windows (Teredo, 6to4, IP-HTTPS) — не VPN, даже при типе «туннель»",
            !Typed("Teredo Tunneling Pseudo-Interface", "Teredo Tunneling Pseudo-Interface", tunnel: true).LooksLikeVpn
            && !Typed("6to4 Adapter", "Microsoft 6to4 Adapter", tunnel: true).LooksLikeVpn
            && !Typed("Microsoft IP-HTTPS Platform Interface", "IP-HTTPS", tunnel: true).LooksLikeVpn);

        var real = FirewallPlanner.SystemAdapters().Where(a => !a.IsLoopback).ToList();
        Console.WriteLine("    на этом компьютере похожи на VPN: " + string.Join(", ", real.Where(a => a.LooksLikeVpn).Select(a => a.Name)));
        Check("на этом компьютере найден хотя бы один адаптер, похожий на VPN", real.Any(a => a.LooksLikeVpn));
    }

    private static async Task VpnNotChosenWarning()
    {
        Console.WriteLine("# Предупреждение, когда VPN-адаптер не выбран или не найден");
        const string app = @"C:\Fake\geoguard_fake_zz.exe";
        var fake = new FakeFirewall();
        using var sync = new FirewallSync(fake, () => SampleAdapters, TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(30));

        var notChosen = FirewallSettings(app);
        notChosen.VpnAdapterMatch = "";
        sync.Request(notChosen, appsBlocked: false);
        await WaitUntil(() => sync.Note is not null);
        Check("адаптер не выбран — предупреждение со ссылкой на вкладку «Сеть»", sync.Note?.Contains("VPN-адаптер не выбран") == true && sync.Note.Contains("«Сеть»"));
        Check("и правил файрвола нет", fake.Last is null || fake.Last.Rules.Count == 0);

        var missing = FirewallSettings(app);
        missing.VpnAdapterMatch = "SomeVpnThatIsNotHere";
        sync.Request(missing, appsBlocked: false);
        await WaitUntil(() => sync.Note?.Contains("не найден") == true);
        Check("адаптер выбран, но такого нет — предупреждение «не найден»", sync.Note?.Contains("«SomeVpnThatIsNotHere» не найден") == true);

        sync.Request(FirewallSettings(app), appsBlocked: false);
        await WaitUntil(() => sync.Note?.Contains("вне VPN") == true);
        Check("адаптер выбран и найден — обычное состояние файрвола без предупреждений",
            sync.Note?.Contains("вне VPN") == true && !sync.Note.Contains("не найден") && !sync.Note.Contains("не выбран"));

        var noCountry = FirewallSettings(app);
        noCountry.VpnAdapterMatch = "";
        noCountry.AllowedCountry = "";
        sync.Request(noCountry, appsBlocked: false);
        await WaitUntil(() => sync.Note is null);
        Check("страна не выбрана — предупреждать не о чем", sync.Note is null);
    }

    private static async Task FirewallSyncBehavior()
    {
        Console.WriteLine("# Синхронизация файрвола (подставной файрвол)");
        const string app = @"C:\Fake\geoguard_fake_zz.exe";
        var fake = new FakeFirewall();
        using var sync = new FirewallSync(fake, () => SampleAdapters, TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(30));

        sync.Request(FirewallSettings(app), appsBlocked: true);
        await WaitUntil(() => fake.Last is not null);
        Check("сеть закрыта: правило на всех интерфейсах", fake.Last!.Rules[0].Interfaces is null);
        Check("заметка сообщает, что сеть закрыта", sync.Note?.Contains("сеть закрыта") == true);

        sync.Request(FirewallSettings(app), appsBlocked: false);
        await WaitUntil(() => fake.Last!.Rules[0].Interfaces is not null);
        Check("после подтверждения страны правило только на интернет-интерфейсах вне VPN", fake.Last!.Rules[0].Interfaces!.Count == 2);
        Check("заметка сообщает про «вне VPN»", sync.Note?.Contains("вне VPN") == true);

        var count = fake.AppliedCount;
        sync.Request(FirewallSettings(app), appsBlocked: false);
        await Task.Delay(200);
        Check("одинаковый запрос не приводит к повторному применению", fake.AppliedCount == count);

        fake.Fail = true;
        sync.Request(FirewallSettings(app), appsBlocked: true);
        await WaitUntil(() => sync.Note?.Contains("сбой") == true);
        Check("ошибка файрвола видна в заметке", sync.Note?.Contains("сбой") == true);
        fake.Fail = false;
        await WaitUntil(() => sync.Note?.Contains("сеть закрыта") == true, timeoutMs: 3000);
        Check("после сбоя применение повторяется само, без нового запроса", sync.Note?.Contains("сеть закрыта") == true && fake.Last!.Rules[0].Interfaces is null);

        var unavailable = new FakeFirewall { Available = false };
        using (var sync2 = new FirewallSync(unavailable, () => SampleAdapters, TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(30)))
        {
            sync2.Request(FirewallSettings(app), appsBlocked: true);
            await WaitUntil(() => sync2.Note is not null);
            Check("без прав администратора файрвол выключен и об этом сказано", sync2.Note?.Contains("Файрвол выключен") == true && unavailable.AppliedCount == 0);

            var noRestriction = FirewallSettings(app);
            noRestriction.AllowedCountry = "";
            sync2.Request(noRestriction, appsBlocked: false);
            await WaitUntil(() => sync2.Note is null);
            Check("если файрвол не нужен, лишних сообщений нет", sync2.Note is null);
        }

        sync.Shutdown();
        Check("при выходе правила удаляются", fake.RemoveAllCalls == 1);
    }

    private static async Task EngineDrivesFirewall()
    {
        Console.WriteLine("# Движок + файрвол: закрытие сети при смене сети/страны, открытие только для VPN");
        const string app = @"C:\Fake\geoguard_fake_zz.exe";
        var signature = "net-A";
        var fake = new FakeFirewall();
        var geo = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("DE")));
        using var sync = new FirewallSync(fake, () => SampleAdapters, TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(30));
        using var engine = new GuardEngine(FirewallSettings(app), geo, () => Volatile.Read(ref signature), Fast, sync);
        engine.Start();

        await WaitUntil(() => fake.Last is not null);
        Check("при старте, пока страна определяется, выход вне VPN закрыт, а сеть через VPN не отрезана",
            fake.Applied.Count > 0 && fake.Applied[0].Rules[0].Interfaces is not null);

        await WaitUntil(() => engine.Status.State == GuardState.Allowed);
        await WaitUntil(() => fake.Last!.Rules[0].Interfaces is not null);
        Check("страна подтверждена: открыт только VPN-адаптер (и внутренние сети)",
            fake.Last!.Rules[0].Interfaces!.SequenceEqual(new[] { "Ethernet", "vEthernet (Виртуальный коммутатор)" }));
        Check("статус содержит заметку о файрволе", engine.Status.FirewallNote?.Contains("вне VPN") == true || engine.Status.Describe().Contains("Файрвол"));

        Volatile.Write(ref signature, "net-B");
        var closed = await WaitUntil(() => fake.Last!.Rules[0].Interfaces is null, timeoutMs: 1000);
        Check("смена сети: сеть приложению сразу закрыта на всех интерфейсах", closed);

        await WaitUntil(() => engine.Status.State == GuardState.Allowed);
        var reopened = await WaitUntil(() => fake.Last!.Rules[0].Interfaces is not null, timeoutMs: 2000);
        Check("после повторного подтверждения снова открыт только VPN", reopened);

        var toUs = FirewallSettings(app);
        toUs.AllowedCountry = "US";
        engine.ApplySettings(toUs);
        var closedForUs = await WaitUntil(() => fake.Last!.Rules[0].Interfaces is null, timeoutMs: 1000);
        Check("страна в настройках сменена на другую — сеть закрыта на всех интерфейсах", closedForUs);
    }

    private static async Task ProtectedProcessIsNeverKilled()
    {
        Console.WriteLine("# Процесс с именем Amnezia не закрывается, даже если попал в список (тестовый процесс, не настоящий VPN)");
        var protectedCopy = Path.Combine(Path.GetDirectoryName(_victimPath)!, "amnezia_geoguard_test.exe");
        File.Copy(_victimPath, protectedCopy, overwrite: true);
        try
        {
            StartProcess(protectedCopy);
            StartVictim();
            await Task.Delay(400);
            Check("оба тестовых процесса запущены", Process.GetProcessesByName("amnezia_geoguard_test").Length == 1 && VictimAlive());

            var settings = DefaultSettings(VictimName);
            settings.RestrictedApps.Add("amnezia_geoguard_test");
            var geo = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("US"))); // чужая страна → идёт блокировка
            using var engine = NewEngine(geo, () => "net", settings);
            engine.Start();

            Check("обычный процесс из списка закрыт", await WaitUntil(() => !VictimAlive(), timeoutMs: 2000));
            await Task.Delay(500);
            Check("процесс «amnezia…» из списка НЕ тронут", Process.GetProcessesByName("amnezia_geoguard_test").Length == 1);
        }
        finally
        {
            foreach (var process in Process.GetProcessesByName("amnezia_geoguard_test"))
            {
                try { process.Kill(); } catch (Exception) { /* уже завершился */ }
                process.Dispose();
            }

            KillVictims();
            await Task.Delay(300);
            try { File.Delete(protectedCopy); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void TaskSchedulerXml()
    {
        Console.WriteLine("# Задача автозапуска: XML для планировщика");
        const string exe = @"C:\Program Files\Geo & Guard\<GeoGuard>.exe";
        const string user = @"DESKTOP-ABC\User";
        var xml = AutoStart.BuildTaskXml(exe, user);
        var reparsed = System.Xml.Linq.XDocument.Parse(xml.ToString()); // экранирование корректно: разбор не падает
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Check("корневой элемент Task в пространстве имён планировщика", reparsed.Root?.Name == ns + "Task");
        Check("запуск с наивысшими правами", reparsed.Descendants(ns + "RunLevel").Single().Value == "HighestAvailable");
        Check("триггер — вход пользователя", reparsed.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")?.Value == user);
        Check("команда — путь к exe в кавычках, спецсимволы сохранены", reparsed.Descendants(ns + "Command").Single().Value == $"\"{exe}\"");
        Check("рабочая папка — папка exe", reparsed.Descendants(ns + "WorkingDirectory").Single().Value == @"C:\Program Files\Geo & Guard");
        Check("без ограничения времени и при работе от батареи", reparsed.Descendants(ns + "ExecutionTimeLimit").Single().Value == "PT0S"
            && reparsed.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value == "false");
    }

    private static void FirewallComReadOnly()
    {
        Console.WriteLine("# COM-API файрвола на этой машине (только чтение, правила не создаются и не меняются)");
        try
        {
            var everything = new WindowsFirewall("");
            var total = everything.CountOwnedRules();
            Console.WriteLine($"    правил в политике файрвола (перечисление через dynamic-COM): {total}");
            Check("перечисление правил через COM работает", total > 0);

            Check("наших тестовых правил в системе нет", new WindowsFirewall("GeoGuard-unittest-never-exists: ").CountOwnedRules() == 0);

            // Объект правила создаётся и наполняется в памяти, в систему не добавляется.
            // Имена берём так же, как в боевом плане — из списка адаптеров с IP-привязкой (файрвол принимает только такие).
            var names = FirewallPlanner.SystemAdapters().Where(a => !a.IsLoopback).Select(a => a.Name).ToArray();
            Check("в системе есть сетевые адаптеры для проверки", names.Length > 0);
            Console.WriteLine($"    адаптеров с IP-привязкой (без loopback): {names.Length}");

            dynamic rule = WindowsFirewall.CreateRuleObject("GeoGuard-unittest: probe", new FirewallRuleSpec(@"C:\Fake\geoguard_fake_zz.exe", names));
            Check("свойства правила принимаются и читаются обратно (имя, программа, исходящее, блокировка, включено)",
                (string)rule.Name == "GeoGuard-unittest: probe" && (string)rule.ApplicationName == @"C:\Fake\geoguard_fake_zz.exe"
                && (int)rule.Direction == 2 && (int)rule.Action == 0 && (bool)rule.Enabled);
            var interfaces = ((System.Collections.IEnumerable)rule.Interfaces).Cast<object>().Select(o => o.ToString()).ToArray();
            Check("ВСЕ имена реальных адаптеров принимаются одним списком и читаются обратно", interfaces.OrderBy(n => n).SequenceEqual(names.OrderBy(n => n)));

            var rejected = new List<string>();
            foreach (var name in names)
            {
                try { WindowsFirewall.CreateRuleObject("GeoGuard-unittest: single", new FirewallRuleSpec(@"C:\Fake\geoguard_fake_zz.exe", new[] { name })); }
                catch (System.Runtime.InteropServices.COMException) { rejected.Add(name); }
            }

            Check("каждое имя по отдельности тоже принимается", rejected.Count == 0);
            if (rejected.Count > 0)
                Console.WriteLine($"    отвергнуты: {string.Join(", ", rejected)}");

            dynamic all = WindowsFirewall.CreateRuleObject("GeoGuard-unittest: probe2", new FirewallRuleSpec(@"C:\Fake\geoguard_fake_zz.exe", null));
            object? none = all.Interfaces;
            Check("правило для всех интерфейсов не содержит списка интерфейсов", none is null || (none is System.Collections.IEnumerable e && !e.Cast<object>().Any()));
        }
        catch (FirewallException ex)
        {
            Check($"COM-API файрвола: {ex.Message}", false);
        }
    }

    private static void ListAdapters()
    {
        var all = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
        Console.WriteLine($"GetAllNetworkInterfaces() в .NET: {all.Length}");
        var probe = new FirewallRuleSpec(@"C:\Fake\geoguard_fake_zz.exe", null);
        var accepted = 0;
        foreach (var nic in all)
        {
            bool ok;
            try
            {
                WindowsFirewall.CreateRuleObject("probe", probe with { Interfaces = new[] { nic.Name } });
                ok = true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                ok = false;
            }

            accepted += ok ? 1 : 0;
            Console.WriteLine($"  {(ok ? "ПРИНЯТ " : "отвергнут")} | {nic.OperationalStatus,-8} | {nic.NetworkInterfaceType,-22} | IPv4={nic.Supports(System.Net.NetworkInformation.NetworkInterfaceComponent.IPv4),-5} IPv6={nic.Supports(System.Net.NetworkInformation.NetworkInterfaceComponent.IPv6),-5} | {nic.Name}");
        }

        Console.WriteLine($"Принято файрволом: {accepted} из {all.Length}");
    }
}
