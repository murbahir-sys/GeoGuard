using System.Diagnostics;
using GeoGuard;

// Запуск: GeoGuard.Tests.exe <путь к geoguard_testproc.exe из проекта GeoGuard.TestVictim>
// «Жертвой» служит собственный процесс с уникальным именем, чтобы случайно не закрыть настоящие программы.

internal static partial class Program
{
    private const string VictimName = "geoguard_testproc";

    private static readonly GuardTimings Fast = new()
    {
        ScanInterval = TimeSpan.FromMilliseconds(20),
        NetworkPollInterval = TimeSpan.FromMilliseconds(20),
        UnverifiedRetryDelay = TimeSpan.FromMilliseconds(40),
        FailingRetryDelay = TimeSpan.FromMilliseconds(80),
        BlockedMaxInterval = TimeSpan.FromMilliseconds(100),
        ConfirmationSpacing = TimeSpan.FromMilliseconds(25),
    };

    private static string _victimPath = "";
    private static int _passed;
    private static int _failed;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1 || !File.Exists(args[0]))
        {
            Console.WriteLine("Укажите путь к geoguard_testproc.exe (проект GeoGuard.TestVictim).");
            return 2;
        }

        _victimPath = args[0];

        // Диагностические режимы (ничего не меняют в системе).
        if (args.Contains("--adapters"))
        {
            ListAdapters();
            return 0;
        }

        if (args.Contains("--geo-live"))
        {
            // Живая проверка сервисов геолокации (нужен интернет, в обычный набор тестов не входит).
            using var geo = new GeoLocationService();
            var answers = await geo.LookupAsync(CancellationToken.None);
            Console.WriteLine($"Ответили сервисов: {answers.Count} из 3");
            foreach (var answer in answers)
                Console.WriteLine($"  {answer.CountryCode} | {answer.CountryName ?? "—"}");
            return answers.Count == 3 ? 0 : 1;
        }

        var dropIndex = Array.IndexOf(args, "--render-adapters");
        if (dropIndex >= 0 && dropIndex + 1 < args.Length)
        {
            RenderAdapterDropDown(args[dropIndex + 1]);
            return 0;
        }

        var renderIndex = Array.IndexOf(args, "--render-ui");
        if (renderIndex >= 0 && renderIndex + 1 < args.Length)
        {
            RenderUi(args[renderIndex + 1]);
            return 0;
        }

        if (args.Contains("--regions"))
        {
            var ui = System.Globalization.CultureInfo.CurrentUICulture;
            Console.WriteLine($"CurrentUICulture={ui.Name}, CurrentCulture={System.Globalization.CultureInfo.CurrentCulture.Name}, InvariantGlobalization={System.Globalization.CultureInfo.CurrentCulture.CompareInfo.GetType().Name}");
            foreach (var code in new[] { "de-DE", "ru-RU", "en-US", "fr-FR", "ja-JP" })
            {
                var r = new System.Globalization.RegionInfo(code);
                Console.WriteLine($"{code}: Display='{r.DisplayName}' English='{r.EnglishName}' Система='{GeoGuard.CountryNames.Localized(r.TwoLetterISORegionName)}'");
            }

            Console.WriteLine("Без региона: " + (GeoGuard.CountryNames.Localized("ZZ") ?? "null") + "; XK=" + (GeoGuard.CountryNames.Localized("XK") ?? "null"));

            return 0;
        }

        if (args.Contains("--com"))
        {
            FirewallComReadOnly();
            Console.WriteLine($"Итого: пройдено {_passed}, провалено {_failed}");
            return _failed == 0 ? 0 : 1;
        }

        KillVictims();

        try
        {
            await StartupIsBlockedUntilConfirmed();
            await NeedsTwoConfirmations();
            await StartupFailuresCloseOnlyAfterTwoInARow();
            await MismatchBlocksImmediately();
            await ProvidersDisagreementBlocks();
            await OneFailureIsTolerated_TwoAreNot();
            await NetworkChangeBlocksAndReverifies();
            await StaleLookupAfterNetworkChangeIsDiscarded();
            await NetworkChangeKillsRunningApp();
            await StartupKillsAlreadyRunningApp();
            await ChangingCountryKillsAndReverifies();
            await EmptyCountryMeansNoRestriction();
            await SlowLookupDoesNotAllowApps();
            AppEntriesAndProtection();
            FirewallPlannerRules();
            VpnAdapterDetection();
            await FirewallSyncBehavior();
            await VpnNotChosenWarning();
            await FirewallFollowsAdapterChanges();
            NetworkSignatureIgnoresLocalVirtualNetworks();
            await EngineIgnoresDockerAndWslButReactsToVpn();
            await EngineDrivesFirewall();
            await ProtectedProcessIsNeverKilled();
            TaskSchedulerXml();
            SettingsFormApplyBehavior();
            CountryFlagsWork();
            StatusPillTexts();
            await GeoServiceRateLimits();
            TabsFitWithoutScrolling();
            FirewallComReadOnly();
            NetworkSignatureIsStable();
        }
        finally
        {
            KillVictims();
        }

        Console.WriteLine();
        Console.WriteLine($"Итого: пройдено {_passed}, провалено {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    // ---- сценарии ----------------------------------------------------------------------------------------------

    private static async Task StartupIsBlockedUntilConfirmed()
    {
        Console.WriteLine("# Старт программы: сначала определяется страна, потом действуем по настройкам");
        var release = new TaskCompletionSource();
        var geo = new FakeGeo(async (_, _) =>
        {
            await release.Task;               // первый ответ о стране ещё не пришёл
            return FakeGeo.In("DE");
        });
        var log = new List<GuardStatus>();
        using var engine = NewEngine(geo, () => "net", DefaultSettings(VictimName), log);

        StartVictim();
        await Task.Delay(300);
        engine.Start();
        await Task.Delay(600);
        Check("пока ответа о стране нет, приложения не закрываются", VictimAlive() && !engine.Status.AppsBlocked);
        Check("состояние — страна определяется", engine.Status.State == GuardState.Unknown && StatusView.Pill(engine.Status).Pill == "Определяем страну…");

        release.SetResult();
        await WaitUntil(() => engine.Status.State == GuardState.Allowed);
        Check("первый ответ — разрешённая страна: приложение продолжает работать", VictimAlive() && !engine.Status.AppsBlocked);
        Check("первый ответ принят сразу, без повторных подтверждений и без промежуточных блокировок",
            geo.Calls == 1 && !Snapshot(log).Any(x => x.AppsBlocked));
        KillVictims();
    }

    private static async Task NeedsTwoConfirmations()
    {
        Console.WriteLine("# После смены сети разрешение возвращается только после двух совпавших проверок подряд");
        var signature = "net-A";
        var geo = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("DE")));
        var log = new List<GuardStatus>();
        using var engine = NewEngine(geo, () => Volatile.Read(ref signature), log: log);
        engine.Start();
        await WaitUntil(() => engine.Status.State == GuardState.Allowed);

        var callsBefore = geo.Calls;
        var logBefore = Snapshot(log).Count;
        Volatile.Write(ref signature, "net-B");
        await WaitUntil(() => engine.Status.AppsBlocked);
        await WaitUntil(() => engine.Status.State == GuardState.Allowed);

        var after = Snapshot(log).Skip(logBefore).ToList();
        Check("между блокировкой и разрешением был статус «идёт подтверждение» (страна видна, приложения закрыты)",
            after.Any(x => x.State == GuardState.Unknown && x.Geo?.CountryCode == "DE" && x.AppsBlocked));
        Check("для возврата разрешения понадобилось не меньше двух проверок", geo.Calls - callsBefore >= 2);
    }

    private static async Task MismatchBlocksImmediately()
    {
        Console.WriteLine("# Другая страна — блокировка по первой же проверке");
        var geo = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("US")));
        var log = new List<GuardStatus>();
        using var engine = NewEngine(geo, () => "net", log: log);
        engine.Start();
        await WaitUntil(() => engine.Status.State == GuardState.Blocking);

        Check("состояние Blocking, приложения закрыты", engine.Status.State == GuardState.Blocking && engine.Status.AppsBlocked);
        Check("страна в статусе — US", engine.Status.Geo?.CountryCode == "US");
        await Task.Delay(300);
        Check("Allowed не наступал ни разу", !Snapshot(log).Any(s => s.State == GuardState.Allowed));
    }

    private static async Task ProvidersDisagreementBlocks()
    {
        Console.WriteLine("# Сервисы расходятся (DE и US) — считается несовпадением");
        var geo = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("DE", "US")));
        var log = new List<GuardStatus>();
        using var engine = NewEngine(geo, () => "net", log: log);
        engine.Start();
        await WaitUntil(() => engine.Status.State == GuardState.Blocking);

        Check("Blocking, показана несовпавшая страна US", engine.Status.State == GuardState.Blocking && engine.Status.Geo?.CountryCode == "US");
        await Task.Delay(300);
        Check("Allowed не наступал ни разу", !Snapshot(log).Any(s => s.State == GuardState.Allowed));
    }

    private static async Task OneFailureIsTolerated_TwoAreNot()
    {
        Console.WriteLine("# Один сбой проверки терпим, два подряд — сбрасывают подтверждение");
        var fail = false;
        var geo = new FakeGeo((_, _) => Task.FromResult(fail ? FakeGeo.In() : FakeGeo.In("DE")));
        var log = new List<GuardStatus>();
        using var engine = NewEngine(geo, () => "net", log: log);
        engine.Start();
        await WaitUntil(() => engine.Status.State == GuardState.Allowed);

        fail = true;
        await WaitUntil(() => engine.Status.State == GuardState.Unknown && engine.Status.AppsBlocked, timeoutMs: 4000);
        Check("после двух сбоев подряд приложения заблокированы", engine.Status.State == GuardState.Unknown && engine.Status.AppsBlocked);
        Check("после первого сбоя состояние ещё оставалось Allowed (с ошибкой в статусе)",
            Snapshot(log).Any(s => s.State == GuardState.Allowed && s.Error is not null));

        fail = false;
        await WaitUntil(() => engine.Status.State == GuardState.Allowed);
        Check("когда сервисы снова отвечают — страна подтверждается заново", engine.Status.State == GuardState.Allowed);
    }

    private static async Task NetworkChangeBlocksAndReverifies()
    {
        Console.WriteLine("# Смена сети: приложения закрываются сразу, потом страна подтверждается заново");
        var signature = "net-A";
        var geo = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("DE")));
        using var engine = NewEngine(geo, () => Volatile.Read(ref signature));
        engine.Start();
        await WaitUntil(() => engine.Status.State == GuardState.Allowed);
        var callsBefore = geo.Calls;

        var sw = Stopwatch.StartNew();
        Volatile.Write(ref signature, "net-B");
        var blocked = await WaitUntil(() => engine.Status.AppsBlocked, timeoutMs: 1000);
        Check($"после смены сети приложения заблокированы за {sw.ElapsedMilliseconds} мс", blocked && sw.ElapsedMilliseconds < 500);

        await WaitUntil(() => engine.Status.State == GuardState.Allowed);
        Check("страна подтвердилась заново", engine.Status.State == GuardState.Allowed);
        Check("для этого выполнено ≥ 2 новых проверок", geo.Calls - callsBefore >= 2);
    }

    private static async Task StaleLookupAfterNetworkChangeIsDiscarded()
    {
        Console.WriteLine("# Запрос, начатый до смены сети, отбрасывается");
        var signature = "net-A";
        var release = new TaskCompletionSource();
        var geo = new FakeGeo(async (call, _) =>
        {
            if (call == 1)
            {
                await release.Task;           // «старый» запрос завис и вернётся уже после смены сети
                return FakeGeo.In("DE");
            }

            return FakeGeo.In("US");          // все новые запросы видят другую страну
        });
        var log = new List<GuardStatus>();
        using var engine = NewEngine(geo, () => Volatile.Read(ref signature), log: log);
        engine.Start();
        await WaitUntil(() => geo.Calls >= 1);

        Volatile.Write(ref signature, "net-B");
        await Task.Delay(200);                // дать движку заметить смену сети
        release.SetResult();
        await WaitUntil(() => engine.Status.State == GuardState.Blocking);
        await Task.Delay(200);

        Check("устаревший ответ DE не попал в статус", !Snapshot(log).Any(s => s.Geo?.CountryCode == "DE"));
        Check("итоговое состояние — Blocking по свежему ответу", engine.Status.State == GuardState.Blocking && engine.Status.Geo?.CountryCode == "US");
    }

    private static async Task NetworkChangeKillsRunningApp()
    {
        Console.WriteLine("# Смена сети закрывает уже запущенное приложение");
        var signature = "net-A";
        var geo = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("DE")));
        using var engine = NewEngine(geo, () => Volatile.Read(ref signature), DefaultSettings(VictimName));
        engine.Start();
        await WaitUntil(() => engine.Status.State == GuardState.Allowed);

        StartVictim();
        await Task.Delay(300);
        Check("пока страна подтверждена, приложение живёт", VictimAlive());

        var sw = Stopwatch.StartNew();
        Volatile.Write(ref signature, "net-B");
        var gone = await WaitUntil(() => !VictimAlive(), timeoutMs: 2000);
        Check($"после смены сети приложение закрыто за {sw.ElapsedMilliseconds} мс", gone && sw.ElapsedMilliseconds < 500);
        KillVictims();
    }

    private static async Task StartupKillsAlreadyRunningApp()
    {
        Console.WriteLine("# Приложение, запущенное до старта GeoGuard: решает первый ответ о стране");
        StartVictim();
        await Task.Delay(300);
        var started = VictimAlive();
        Check("приложение запущено", started);
        if (!started)
            Console.WriteLine($"    диагностика: {VictimDiagnostics()}");

        // Первый ответ — другая страна: закрывается сразу.
        var geoUs = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("US")));
        using (var engine = NewEngine(geoUs, () => "net", DefaultSettings(VictimName)))
        {
            var sw = Stopwatch.StartNew();
            engine.Start();
            var gone = await WaitUntil(() => !VictimAlive(), timeoutMs: 2000);
            Check($"первый ответ — US при разрешённой DE: приложение закрыто за {sw.ElapsedMilliseconds} мс", gone && sw.ElapsedMilliseconds < 700);
        }

        KillVictims();
        await Task.Delay(300);

        // Первый ответ — разрешённая страна: приложение не трогаем.
        StartVictim();
        await Task.Delay(300);
        var geoDe = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("DE")));
        using (var engine = NewEngine(geoDe, () => "net", DefaultSettings(VictimName)))
        {
            engine.Start();
            await WaitUntil(() => engine.Status.State == GuardState.Allowed);
            await Task.Delay(500);
            Check("первый ответ — DE при разрешённой DE: приложение продолжает работать", VictimAlive());
        }

        KillVictims();
    }

    private static async Task ChangingCountryKillsAndReverifies()
    {
        Console.WriteLine("# Смена разрешённой страны в настройках");
        var geo = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("DE")));
        using var engine = NewEngine(geo, () => "net", DefaultSettings(VictimName));
        engine.Start();
        await WaitUntil(() => engine.Status.State == GuardState.Allowed);
        StartVictim();
        await Task.Delay(300);

        var toUs = DefaultSettings(VictimName);
        toUs.AllowedCountry = "US";
        engine.ApplySettings(toUs);
        // Подтверждение страны сброшено (состояние Unknown), пока свежая проверка не покажет US ≠ DE — но приложения закрыты сразу.
        Check("сразу после смены на US приложения закрыты", engine.Status.AppsBlocked);
        Check("приложение закрыто", await WaitUntil(() => !VictimAlive(), timeoutMs: 1000));

        var toDe = DefaultSettings(VictimName);
        toDe.AllowedCountry = "DE";
        engine.ApplySettings(toDe);
        Check("возврат на DE не разрешает приложения мгновенно (нужно подтвердить заново)", engine.Status.AppsBlocked);
        await WaitUntil(() => engine.Status.State == GuardState.Allowed);
        Check("через пару проверок приложения снова разрешены", engine.Status.State == GuardState.Allowed);
        KillVictims();
    }

    private static async Task EmptyCountryMeansNoRestriction()
    {
        Console.WriteLine("# Страна не выбрана — ограничений нет");
        var geo = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("US")));
        var settings = DefaultSettings(VictimName);
        settings.AllowedCountry = "";
        using var engine = NewEngine(geo, () => "net", settings);
        engine.Start();
        Check("приложения не заблокированы с первой секунды", !engine.Status.AppsBlocked);

        StartVictim();
        await Task.Delay(600);
        Check("приложение продолжает работать", VictimAlive());
        KillVictims();
    }

    private static async Task SlowLookupDoesNotAllowApps()
    {
        Console.WriteLine("# Зависшие проверки: окно ожидания заканчивается, и приложения закрываются");
        var geo = new FakeGeo(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeGeo.In("DE");
        });
        var timings = Fast with { StartupGraceTimeout = TimeSpan.FromMilliseconds(1500) };
        using var engine = new GuardEngine(DefaultSettings(), geo, () => "net", timings);
        engine.Start();
        await Task.Delay(300);
        var early = engine.Status;
        Check("в начале окна ожидания приложения не закрыты", !early.AppsBlocked);
        if (early.AppsBlocked)
            Console.WriteLine($"    диагностика: state={early.State}, error={early.Error}, calls={geo.Calls}");
        var blocked = await WaitUntil(() => engine.Status.AppsBlocked, timeoutMs: 5000);
        Check("ответа нет слишком долго — приложения закрыты, страна не подтверждена", blocked && engine.Status.State == GuardState.Unknown);
    }

    private static async Task StartupFailuresCloseOnlyAfterTwoInARow()
    {
        Console.WriteLine("# Старт: страну не удалось получить — закрываем только после двух неудач подряд");
        // Одна неудача, затем успех: приложение не закрывается.
        var calls = 0;
        var geoOne = new FakeGeo((_, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1 ? FakeGeo.In() : FakeGeo.In("DE")));
        var log = new List<GuardStatus>();
        using (var engine = NewEngine(geoOne, () => "net", log: log))
        {
            engine.Start();
            await WaitUntil(() => engine.Status.State == GuardState.Allowed);
            Check("одна неудача при старте, затем DE: приложения не блокировались",
                engine.Status.State == GuardState.Allowed && !Snapshot(log).Any(x => x.AppsBlocked));
        }

        // Две неудачи подряд: закрываем. Вторая попытка ждёт разрешения теста, чтобы проверить состояние после первой.
        var gate = new TaskCompletionSource();
        var geoTwo = new FakeGeo(async (call, _) =>
        {
            if (call >= 2)
                await gate.Task;
            return FakeGeo.In();
        });
        using (var engine = NewEngine(geoTwo, () => "net"))
        {
            engine.Start();
            await WaitUntil(() => engine.Status.Error is not null && geoTwo.Calls >= 2);
            Check("после первой неудачи приложения ещё не закрыты", !engine.Status.AppsBlocked);
            gate.SetResult();
            var blocked = await WaitUntil(() => engine.Status.AppsBlocked, timeoutMs: 3000);
            Check("после второй неудачи подряд приложения закрыты", blocked && engine.Status.State == GuardState.Unknown);
        }
    }

    private static void NetworkSignatureIsStable()
    {
        Console.WriteLine("# Отпечаток сети на этой машине стабилен (нет ложных срабатываний)");
        var first = NetworkSignature.Compute("Amnezia");
        var distinct = new HashSet<string> { first };
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            distinct.Add(NetworkSignature.Compute("Amnezia"));
            Thread.Sleep(250);
        }

        var watched = FirewallPlanner.SystemAdapters()
            .Where(a => a.IsUp && !a.IsLoopback && (a.HasGateway || VpnMatch.IsMatch(a, "Amnezia")))
            .Select(a => a.Name)
            .ToList();
        var all = FirewallPlanner.SystemAdapters().Count;
        Console.WriteLine($"    адаптеров с IP: {all}, в отпечатке (VPN + со шлюзом): {watched.Count} — {string.Join("; ", watched)}");
        Console.WriteLine($"    различных значений отпечатка за 10 с: {distinct.Count}");
        Check("отпечаток не менялся за 10 секунд", distinct.Count == 1);
        Check("в отпечатке есть адаптер с выходом в интернет", watched.Count > 0);
        Check("виртуальные сети (WSL, Default Switch, VMware) в отпечаток не входят",
            !watched.Any(n => n.Contains("WSL", StringComparison.OrdinalIgnoreCase) || n.Contains("Default Switch", StringComparison.OrdinalIgnoreCase) || n.Contains("VMware", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task FirewallFollowsAdapterChanges()
    {
        Console.WriteLine("# Файрвол сам следит за составом адаптеров (запуск/остановка WSL, Wi-Fi) без запросов движка");
        const string app = @"C:\Fake\geoguard_fake_zz.exe";
        IReadOnlyList<AdapterInfo> adapters = new[] { Uplink, VpnAdapter, Wsl, Loopback };
        var fake = new FakeFirewall();
        using var sync = new FirewallSync(fake, () => Volatile.Read(ref adapters), TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(30));

        sync.Request(FirewallSettings(app), appsBlocked: false);
        await WaitUntil(() => fake.Last is not null);
        Check("WSL работает: закрыт только реальный выход", fake.Last!.Rules[0].Interfaces!.SequenceEqual(new[] { "vEthernet (Виртуальный коммутатор)" }));

        var applied = fake.AppliedCount;
        Volatile.Write(ref adapters, new[] { Uplink, VpnAdapter, Wsl with { IsUp = true, Ipv4 = new[] { "172.20.9.1" } }, Loopback });
        await Task.Delay(300);
        Check("WSL перезапустился с другим адресом — правило не менялось", fake.AppliedCount == applied);

        Volatile.Write(ref adapters, new[] { Uplink, VpnAdapter, Wsl with { IsUp = false }, Loopback });
        await WaitUntil(() => fake.Last!.Rules[0].Interfaces!.Count == 2);
        Check("WSL остановлен: его адаптер добавлен в правило без запроса движка", SameSet(fake.Last!.Rules[0].Interfaces!, "vEthernet (Виртуальный коммутатор)", "vEthernet (WSL)"));

        Volatile.Write(ref adapters, new[] { Uplink, VpnAdapter, Wsl, Loopback });
        await WaitUntil(() => fake.Last!.Rules[0].Interfaces!.Count == 1);
        Check("WSL снова запущен: правило вернулось к прежнему", fake.Last!.Rules[0].Interfaces!.SequenceEqual(new[] { "vEthernet (Виртуальный коммутатор)" }));

        var usb = Adapter("Ethernet 5", "USB Ethernet", gateway: true, ip: "192.168.7.2");
        Volatile.Write(ref adapters, new[] { Uplink, VpnAdapter, Wsl, usb, Loopback });
        await WaitUntil(() => fake.Last!.Rules[0].Interfaces!.Contains("Ethernet 5"));
        Check("подключили USB-модем со шлюзом: он закрыт правилом автоматически", fake.Last!.Rules[0].Interfaces!.Contains("Ethernet 5"));
    }

    private static void NetworkSignatureIgnoresLocalVirtualNetworks()
    {
        Console.WriteLine("# Отпечаток сети: только VPN и адаптеры со шлюзом; WSL/Docker/VMware не в счёт");
        var baseline = new[] { Uplink, VpnAdapter, Wsl, DefaultSwitch, VmNet, DisconnectedNic, Loopback };
        var sig = NetworkSignature.Compute(baseline, "Amnezia");
        string Sig(params AdapterInfo[] a) => NetworkSignature.Compute(a, "Amnezia");

        var docker = Adapter("vEthernet (Docker NAT)", "Hyper-V Virtual Ethernet Adapter #4", ip: "172.30.0.1");
        Check("порядок адаптеров не важен", Sig(baseline.Reverse().ToArray()) == sig);
        Check("запустили Docker (появился новый адаптер) — отпечаток тот же", Sig(baseline.Append(docker).ToArray()) == sig);
        Check("остановили WSL (адаптер пропал) — отпечаток тот же", Sig(baseline.Where(a => a != Wsl).ToArray()) == sig);
        Check("WSL перезапущен с другим адресом — отпечаток тот же", Sig(baseline.Select(a => a == Wsl ? a with { Ipv4 = new[] { "172.20.9.1" } } : a).ToArray()) == sig);
        Check("виртуальная машина VMware остановлена — отпечаток тот же", Sig(baseline.Select(a => a == VmNet ? a with { IsUp = false } : a).ToArray()) == sig);
        Check("отключённая сетевая карта включилась без шлюза — отпечаток тот же", Sig(baseline.Select(a => a == DisconnectedNic ? a with { IsUp = true } : a).ToArray()) == sig);

        Check("VPN выключен (адаптер Down) — отпечаток изменился", Sig(baseline.Select(a => a == VpnAdapter ? a with { IsUp = false } : a).ToArray()) != sig);
        Check("VPN-адаптер исчез — отпечаток изменился", Sig(baseline.Where(a => a != VpnAdapter).ToArray()) != sig);
        Check("VPN получил другой адрес — отпечаток изменился", Sig(baseline.Select(a => a == VpnAdapter ? a with { Ipv4 = new[] { "10.8.2.1" } } : a).ToArray()) != sig);
        Check("сменился шлюз реальной сети — отпечаток изменился", Sig(baseline.Select(a => a == Uplink ? a with { Gateways = new[] { "10.0.7.1" } } : a).ToArray()) != sig);
        Check("пропала реальная сеть — отпечаток изменился", Sig(baseline.Where(a => a != Uplink).ToArray()) != sig);
        Check("появился Wi-Fi со шлюзом — отпечаток изменился", Sig(baseline.Append(Adapter("Wi-Fi", wireless: true, gateway: true, ip: "192.168.1.5")).ToArray()) != sig);

        var noVpnPattern = NetworkSignature.Compute(baseline, "");
        Check("без шаблона VPN следим только за адаптерами со шлюзом: падение VPN не влияет",
            NetworkSignature.Compute(baseline.Select(a => a == VpnAdapter ? a with { IsUp = false } : a).ToArray(), "") == noVpnPattern);
        Check("без шаблона VPN смена реальной сети всё равно видна",
            NetworkSignature.Compute(baseline.Where(a => a != Uplink).ToArray(), "") != noVpnPattern);
    }

    private static async Task EngineIgnoresDockerAndWslButReactsToVpn()
    {
        Console.WriteLine("# Движок: запуск/остановка WSL и Docker ничего не закрывают, падение VPN — закрывает");
        IReadOnlyList<AdapterInfo> adapters = new[] { Uplink, VpnAdapter, Wsl, Loopback };
        var geo = new FakeGeo((_, _) => Task.FromResult(FakeGeo.In("DE")));
        var log = new List<GuardStatus>();
        using var engine = NewEngine(geo, () => NetworkSignature.Compute(Volatile.Read(ref adapters), "Amnezia"), DefaultSettings(VictimName), log);
        engine.Start();
        await WaitUntil(() => engine.Status.State == GuardState.Allowed);

        StartVictim();
        await Task.Delay(300);
        Check("приложение работает", VictimAlive());

        var statusesBefore = Snapshot(log).Count;
        var docker = Adapter("vEthernet (Docker NAT)", ip: "172.30.0.1");
        Volatile.Write(ref adapters, new[] { Uplink, VpnAdapter, Wsl, docker, Loopback });          // Docker запущен
        await Task.Delay(200);
        Volatile.Write(ref adapters, new[] { Uplink, VpnAdapter, Loopback });                        // WSL и Docker остановлены
        await Task.Delay(200);
        Volatile.Write(ref adapters, new[] { Uplink, VpnAdapter, Wsl with { Ipv4 = new[] { "172.20.9.1" } }, Loopback }); // WSL запущен снова
        await Task.Delay(600);
        Check("приложение продолжает работать после запуска/остановки WSL и Docker", VictimAlive());
        Check("состояние всё это время Allowed, блокировок не было", engine.Status.State == GuardState.Allowed
            && !Snapshot(log).Skip(statusesBefore).Any(s => s.AppsBlocked));

        var sw = Stopwatch.StartNew();
        Volatile.Write(ref adapters, new[] { Uplink, Wsl, Loopback });                               // VPN упал
        var closed = await WaitUntil(() => !VictimAlive(), timeoutMs: 2000);
        Check($"VPN упал — приложение закрыто за {sw.ElapsedMilliseconds} мс", closed && sw.ElapsedMilliseconds < 500);
        Check("состояние — страна не подтверждена, приложения заблокированы", engine.Status.AppsBlocked);

        Volatile.Write(ref adapters, new[] { Uplink, VpnAdapter, Wsl, Loopback });                   // VPN вернулся
        var back = await WaitUntil(() => engine.Status.State == GuardState.Allowed, timeoutMs: 3000);
        Check("VPN вернулся и страна подтверждена заново — приложения снова разрешены", back);
        KillVictims();
    }

    // ---- вспомогательное ---------------------------------------------------------------------------------------

    private sealed class FakeFirewall : IFirewall
    {
        private readonly object _lock = new();
        public bool Available = true;
        public volatile bool Fail;
        public int RemoveAllCalls;
        public readonly List<FirewallPlan> Applied = new();

        public bool IsAvailable => Available;
        public string? UnavailableReason => Available ? null : "нет прав администратора";
        public int AppliedCount { get { lock (_lock) return Applied.Count; } }
        public FirewallPlan? Last { get { lock (_lock) return Applied.LastOrDefault(); } }

        public void Apply(FirewallPlan plan)
        {
            if (Fail)
                throw new FirewallException("сбой файрвола");
            lock (_lock)
                Applied.Add(plan);
        }

        public void RemoveAll() => Interlocked.Increment(ref RemoveAllCalls);
    }

    private static void StartProcess(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(path)! })?.Dispose();

    private static AppSettings DefaultSettings(string app = "geoguard_nonexistent_app") => new()
    {
        AllowedCountry = "DE",
        RestrictedApps = new List<string> { app },
        CheckIntervalSeconds = 1,
    };

    private static GuardEngine NewEngine(IGeoLookup geo, Func<string> signature, AppSettings? settings = null, List<GuardStatus>? log = null)
    {
        var engine = new GuardEngine(settings ?? DefaultSettings(), geo, signature, Fast);
        if (log is not null)
            engine.StatusChanged += status => { lock (log) log.Add(status); };
        return engine;
    }

    private static List<GuardStatus> Snapshot(List<GuardStatus> log)
    {
        lock (log)
            return log.ToList();
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
                return true;
            await Task.Delay(5);
        }

        return condition();
    }

    private static void Check(string name, bool ok)
    {
        if (ok)
            _passed++;
        else
            _failed++;
        Console.WriteLine($"  [{(ok ? "OK" : "ПРОВАЛ")}] {name}");
    }

    private static Process? _lastVictim;

    private static string VictimDiagnostics() =>
        _lastVictim is null ? "процесс не запускался" : _lastVictim.HasExited ? $"завершён, код {_lastVictim.ExitCode:X} (FFFFFFFF — это Kill)" : "ещё работает";

    private static void StartVictim()
    {
        // В первые миллисекунды после завершения предыдущей копии Windows отказывает в запуске того же файла — повторяем.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _lastVictim = Process.Start(new ProcessStartInfo(_victimPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_victimPath)!,
                });
                return;
            }
            catch (System.ComponentModel.Win32Exception) when (attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static bool VictimAlive() => Process.GetProcessesByName(VictimName).Length > 0;

    private static void KillVictims()
    {
        foreach (var process in Process.GetProcessesByName(VictimName))
        {
            try { process.Kill(); } catch (Exception) { /* уже завершился */ }
            process.Dispose();
        }
    }

    private sealed class FakeGeo(Func<int, CancellationToken, Task<IReadOnlyList<GeoResult>>> handler) : IGeoLookup
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyList<GeoResult>> LookupAsync(CancellationToken ct) => handler(Interlocked.Increment(ref _calls), ct);

        public void Dispose()
        {
        }

        public static IReadOnlyList<GeoResult> In(params string[] codes) =>
            codes.Select(c => new GeoResult(c, c, "203.0.113.1")).ToList();
    }
}
