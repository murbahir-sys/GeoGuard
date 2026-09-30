using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace GeoGuard;

public enum GuardState
{
    /// <summary>Страна не подтверждена: ещё не определена, идёт подтверждение или проверка не удаётся.</summary>
    Unknown,
    /// <summary>Страна подтверждена и совпадает с разрешённой (или ограничение выключено) — приложения можно запускать.</summary>
    Allowed,
    /// <summary>Хотя бы один сервис назвал страну, отличную от разрешённой — приложения из списка закрываются.</summary>
    Blocking,
}

/// <param name="AppsBlocked">Закрываются ли сейчас приложения из списка. Верно и для Blocking, и для Unknown при выбранной стране.</param>
/// <param name="FirewallNote">Состояние файрвола (или его проблема) для показа пользователю; null — файрвол не используется.</param>
public sealed record GuardStatus(GuardState State, GeoResult? Geo, DateTime? CheckedAt, string? Error, bool AppsBlocked, string? FirewallNote = null)
{
    /// <summary>Например, «Германия (DE)»: название на языке Windows, иначе то, что вернул сервис геолокации.</summary>
    public string? CountryLabel
    {
        get
        {
            if (Geo is null)
                return null;

            var name = CountryNames.Localized(Geo.CountryCode);
            if (string.IsNullOrEmpty(name))
                name = Geo.CountryName;
            return string.IsNullOrEmpty(name) ? Geo.CountryCode : $"{name} ({Geo.CountryCode})";
        }
    }

    public string Describe()
    {
        var text = CountryLabel is { } country
            ? State == GuardState.Unknown
                ? $"Текущая страна по IP: {country} — идёт подтверждение, приложения заблокированы"
                : $"Текущая страна по IP: {country} — приложения {(AppsBlocked ? "заблокированы" : "разрешены")}"
            : AppsBlocked ? "Текущая страна по IP не определена — приложения заблокированы" : "Текущая страна по IP ещё не определена";
        if (Error is not null)
            text += $"\n{Error}";
        if (FirewallNote is not null)
            text += $"\n{FirewallNote}";
        return text;
    }
}

/// <summary>Интервалы работы движка. Значения по умолчанию — рабочие; тесты подставляют короткие.</summary>
public sealed record GuardTimings
{
    /// <summary>Как часто искать и закрывать запрещённые процессы.</summary>
    public TimeSpan ScanInterval { get; init; } = TimeSpan.FromMilliseconds(200);
    /// <summary>Как часто сверять отпечаток сети (дополнительно к событиям Windows).</summary>
    public TimeSpan NetworkPollInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    /// <summary>Пауза между проверками, пока страна не подтверждена.</summary>
    public TimeSpan UnverifiedRetryDelay { get; init; } = TimeSpan.FromMilliseconds(1500);
    /// <summary>Пауза между проверками, когда сервисы геолокации не отвечают.</summary>
    public TimeSpan FailingRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Верхняя граница интервала проверки, пока страна не совпадает с разрешённой.</summary>
    public TimeSpan BlockedMaxInterval { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Минимальный промежуток между двумя проверками, засчитываемыми как подтверждение.</summary>
    public TimeSpan ConfirmationSpacing { get; init; } = TimeSpan.FromSeconds(1);
    /// <summary>Сколько после запуска программы ждать первого ответа о стране, не закрывая приложения. Страховка от зависших проверок.</summary>
    public TimeSpan StartupGraceTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Разрешает приложения из списка только в одной стране и закрывает их во всех остальных случаях.
/// Работает «по умолчанию закрыто»: приложения разрешены, лишь пока страна подтверждена.
/// Единственное исключение — запуск самой программы: пока не пришёл первый ответ о стране, приложения не трогаем,
/// а дальше решает этот ответ. События вызываются из фоновых потоков.
/// </summary>
/// <remarks>
/// Жёсткие правила:
/// - Страна подтверждена только когда ВСЕ ответившие сервисы называют разрешённую страну.
/// - Любая смена сети (VPN, адаптер, шлюз) сразу сбрасывает подтверждение и закрывает приложения;
///   заново разрешить их можно лишь после <see cref="ConfirmationsRequired"/> совпавших проверок подряд.
///   Проверка, начатая до смены сети, отбрасывается — она могла относиться к старому маршруту.
/// - <see cref="MaxConsecutiveFailures"/> неудачных проверок подряд тоже сбрасывают подтверждение.
/// - Несовпадение страны принимается по первой же проверке.
/// - При запуске программы (окно ожидания): первый успешный ответ принимается сразу, без повторных подтверждений;
///   приложения закрываются, только если ответ показал другую страну, страну не удалось получить
///   <see cref="MaxConsecutiveFailures"/> раза подряд или ответа нет дольше <see cref="GuardTimings.StartupGraceTimeout"/>.
/// </remarks>
public sealed class GuardEngine : IDisposable
{
    private const int ConfirmationsRequired = 2;
    private const int MaxConsecutiveFailures = 2;

    private readonly IGeoLookup _geo;
    private readonly Func<string> _networkSignature;
    private readonly GuardTimings _timings;
    private readonly FirewallSync? _firewall;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly object _gate = new();
    private readonly object _networkLock = new();
    private readonly object _killLock = new();

    // Состояние проверки страны, под _gate (сам объект настроек после передачи не меняется).
    private volatile AppSettings _settings;
    private IReadOnlyList<GeoResult>? _geos;        // подтверждённый ответ сервисов; null — не подтверждён
    private IReadOnlyList<GeoResult>? _tentative;   // Allowed-ответ, который ещё ждёт повторных подтверждений
    private int _confirmations;
    private long _lastConfirmationTimestamp;        // 0 — подтверждений ещё не было
    private int _failures;
    private bool _startupGrace = true;              // пока true — ждём первого ответа о стране и ничего не закрываем
    private long _graceStartedTimestamp;
    private string? _error;
    private DateTime? _checkedAt;
    private GuardStatus _status = new(GuardState.Unknown, null, null, null, false);

    private string? _networkSignatureValue;         // под _networkLock
    private int _epoch;                             // растёт при каждой смене сети
    private volatile string[] _restrictedApps;
    private volatile bool _blocking;
    private volatile bool _disposed;
    private volatile bool _inStartupGrace = true;   // копия _startupGrace для проверки без блокировки

    public event Action<GuardStatus>? StatusChanged;
    public event Action<string>? AppBlocked;

    /// <param name="firewall">Необязательно: если задан, вместе с закрытием приложений держит для них правила файрвола. Движок им не владеет.</param>
    public GuardEngine(
        AppSettings settings,
        IGeoLookup? geo = null,
        Func<string>? networkSignature = null,
        GuardTimings? timings = null,
        FirewallSync? firewall = null)
    {
        _settings = settings;
        _restrictedApps = settings.ProcessNames();
        _geo = geo ?? new GeoLocationService();
        _networkSignature = networkSignature ?? (() => NetworkSignature.Compute(_settings.VpnAdapterMatch));
        _timings = timings ?? new GuardTimings();
        _firewall = firewall;
    }

    public GuardStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public void Start()
    {
        lock (_networkLock)
            _networkSignatureValue = ReadNetworkSignature();

        if (_firewall is not null)
            _firewall.NoteChanged += OnFirewallNote;

        // Окно ожидания: страну ещё не знаем — приложения не трогаем, пока не придёт первый ответ.
        lock (_gate)
            _graceStartedTimestamp = Stopwatch.GetTimestamp();
        Publish();

        NetworkChange.NetworkAddressChanged += OnNetworkEvent;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkEvent;

        _ = Task.Run(() => GeoLoopAsync(_cts.Token));
        _ = Task.Run(() => ScanLoopAsync(_cts.Token));
        _ = Task.Run(() => NetworkLoopAsync(_cts.Token));
    }

    public void ApplySettings(AppSettings settings)
    {
        // _networkLock снаружи: пока настройки и базовый отпечаток сети меняются, проверка сети не должна принять это за смену сети.
        lock (_networkLock)
        {
            bool vpnMatchChanged;
            lock (_gate)
            {
                // Другая разрешённая страна — прежнее подтверждение недействительно, нужно подтвердить заново.
                if (!string.Equals(_settings.AllowedCountry, settings.AllowedCountry, StringComparison.OrdinalIgnoreCase))
                    ResetVerification();

                vpnMatchChanged = !string.Equals(_settings.VpnAdapterMatch, settings.VpnAdapterMatch, StringComparison.Ordinal);
                _settings = settings;
                _restrictedApps = settings.ProcessNames();
                _tentative = null;
                _confirmations = 0;
            }

            // Другой шаблон VPN меняет состав отпечатка, но это не смена сети — берём отпечаток заново.
            if (vpnMatchChanged)
                _networkSignatureValue = ReadNetworkSignature();
        }

        Publish();
        TriggerCheck();
    }

    public void TriggerCheck()
    {
        try
        {
            _wake.Release();
        }
        catch (Exception ex) when (ex is SemaphoreFullException or ObjectDisposedException)
        {
            // Проверка уже запрошена или движок остановлен.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_firewall is not null)
            _firewall.NoteChanged -= OnFirewallNote;
        NetworkChange.NetworkAddressChanged -= OnNetworkEvent;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkEvent;
        _cts.Cancel();

        // Дожидаемся уже идущего прохода по процессам: после Dispose движок ничего закрывать не должен.
        lock (_killLock)
        {
        }

        _geo.Dispose();
        _cts.Dispose();
    }

    private void OnNetworkEvent(object? sender, EventArgs e) => CheckNetwork();

    private void OnFirewallNote(string? note) => Publish();

    private string? ReadNetworkSignature()
    {
        try
        {
            return _networkSignature();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    /// <summary>Сверяет отпечаток сети с предыдущим; при изменении сбрасывает подтверждение страны.</summary>
    private void CheckNetwork()
    {
        bool changed;
        lock (_networkLock)
        {
            var current = ReadNetworkSignature();
            if (current is null)
                return;

            changed = _networkSignatureValue is not null && _networkSignatureValue != current;
            _networkSignatureValue = current;
        }

        if (!changed)
            return;

        Interlocked.Increment(ref _epoch);
        lock (_gate)
            ResetVerification();

        // Внешний IP мог измениться: приложения закрываются сразу, страна проверяется заново.
        Publish();
        TriggerCheck();
    }

    /// <summary>Полный сброс: страна не подтверждена, счётчики и ошибка обнулены.</summary>
    private void ResetVerification()
    {
        DropVerification();
        _failures = 0;
        _error = null;
    }

    private void DropVerification()
    {
        _geos = null;
        _tentative = null;
        _confirmations = 0;
        _lastConfirmationTimestamp = 0;
    }

    private async Task GeoLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var epoch = Volatile.Read(ref _epoch);
            try
            {
                var results = await _geo.LookupAsync(ct);

                // Сеть сменилась, пока шёл запрос: ответ мог прийти по старому маршруту — не доверяем ему.
                if (epoch != Volatile.Read(ref _epoch))
                    continue;

                if (results.Count == 0)
                    OnLookupFailed("Не удалось определить страну по IP");
                else
                    OnLookup(results);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (epoch == Volatile.Read(ref _epoch))
                    OnLookupFailed($"Ошибка проверки: {ex.Message}");
            }

            try
            {
                await _wake.WaitAsync(NextDelay(), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private TimeSpan NextDelay()
    {
        lock (_gate)
        {
            if (_failures >= MaxConsecutiveFailures)
                return _timings.FailingRetryDelay;

            // Пока страна не подтверждена (или идёт подтверждение, или был сбой) — проверяем часто.
            if (_failures > 0 || _geos is null || _confirmations > 0)
                return _timings.UnverifiedRetryDelay;

            var interval = TimeSpan.FromSeconds(_settings.CheckIntervalSeconds);
            return Evaluate(_geos) == GuardState.Blocking && interval > _timings.BlockedMaxInterval
                ? _timings.BlockedMaxInterval
                : interval;
        }
    }

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_timings.ScanInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                CheckStartupGraceTimeout();
                if (_blocking)
                    KillRestricted();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task NetworkLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_timings.NetworkPollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                CheckNetwork();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnLookup(IReadOnlyList<GeoResult> results)
    {
        lock (_gate)
        {
            _failures = 0;
            _error = null;
            _checkedAt = DateTime.Now;

            var firstAnswer = _startupGrace;
            EndStartupGrace();

            var verifiedAllowed = _geos is not null && Evaluate(_geos) == GuardState.Allowed;
            if (firstAnswer || _settings.AllowedCountry.Length == 0 || verifiedAllowed || Evaluate(results) == GuardState.Blocking)
            {
                // Первый ответ после запуска принимается как есть; несовпадение фиксируется сразу;
                // уже подтверждённая страна просто обновляется; при выключенном ограничении подтверждать нечего.
                Accept(results);
            }
            else
            {
                // Страна совпала, но после старта, смены сети, сбоя или смены настроек нужно несколько проверок подряд.
                _tentative = results;
                if (_lastConfirmationTimestamp == 0
                    || Stopwatch.GetElapsedTime(_lastConfirmationTimestamp) >= _timings.ConfirmationSpacing)
                {
                    _confirmations++;
                    _lastConfirmationTimestamp = Stopwatch.GetTimestamp();
                }

                if (_confirmations >= ConfirmationsRequired)
                    Accept(results);
            }
        }

        Publish();
    }

    private void OnLookupFailed(string error)
    {
        lock (_gate)
        {
            _error = error;
            if (++_failures >= MaxConsecutiveFailures)
            {
                DropVerification();
                EndStartupGrace(); // страну не удалось получить два раза подряд — дальше действуем по строгим правилам
            }
        }

        Publish();
    }

    private void EndStartupGrace()
    {
        _startupGrace = false;
        _inStartupGrace = false;
    }

    /// <summary>Страховка: если первого ответа нет слишком долго, окно ожидания заканчивается и включаются строгие правила.</summary>
    private void CheckStartupGraceTimeout()
    {
        if (!_inStartupGrace || Stopwatch.GetElapsedTime(_graceStartedTimestamp) < _timings.StartupGraceTimeout)
            return;

        lock (_gate)
        {
            if (!_startupGrace)
                return;
            EndStartupGrace();
        }

        Publish();
    }

    private void Accept(IReadOnlyList<GeoResult> results)
    {
        _geos = results;
        _tentative = null;
        _confirmations = 0;
        _lastConfirmationTimestamp = 0;
    }

    /// <summary>Пересчитывает состояние и сообщает о нём. Вызывать без удержания _gate.</summary>
    private void Publish()
    {
        GuardStatus status;
        bool startedBlocking;
        AppSettings settings;
        bool blocking;

        lock (_gate)
        {
            var confirming = _confirmations > 0;
            var state = confirming ? GuardState.Unknown : Evaluate(_geos);
            // Ограничение включено (страна выбрана), а страна не подтверждена как разрешённая — приложения закрываются.
            // В окне ожидания при запуске программы страна ещё не определялась — приложения не трогаем.
            blocking = _settings.AllowedCountry.Length > 0 && state != GuardState.Allowed && !_startupGrace;

            settings = _settings;
            startedBlocking = blocking && !_blocking;
            status = new GuardStatus(state, Representative(confirming ? _tentative : _geos), _checkedAt, _error, blocking, _firewall?.Note);
            _status = status;
            _blocking = blocking;
        }

        // Файрвол применяется в своём потоке и не задерживает закрытие приложений.
        _firewall?.Request(settings, blocking);

        // Страна только что перестала быть подтверждённой (например, включили VPN) — закрываем уже запущенные приложения сразу.
        if (startedBlocking)
            KillRestricted();

        StatusChanged?.Invoke(status);
    }

    private GuardState Evaluate(IReadOnlyList<GeoResult>? geos)
    {
        if (geos is null || geos.Count == 0)
            return GuardState.Unknown;

        var allowed = _settings.AllowedCountry;
        return allowed.Length == 0 || geos.All(g => IsAllowed(g, allowed))
            ? GuardState.Allowed
            : GuardState.Blocking;
    }

    /// <summary>Что показывать пользователю: несовпавшая страна, если она есть, иначе первая.</summary>
    private GeoResult? Representative(IReadOnlyList<GeoResult>? geos)
    {
        if (geos is null || geos.Count == 0)
            return null;

        var allowed = _settings.AllowedCountry;
        return geos.FirstOrDefault(g => !IsAllowed(g, allowed)) ?? geos[0];
    }

    private static bool IsAllowed(GeoResult geo, string allowedCountry) =>
        string.Equals(geo.CountryCode, allowedCountry, StringComparison.OrdinalIgnoreCase);

    private void KillRestricted()
    {
        var names = _restrictedApps;
        if (names.Length == 0)
            return;

        lock (_killLock)
        {
            if (_disposed)
                return;

            foreach (var name in names)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (process.Id == Environment.ProcessId || IsProtectedProcess(process))
                            continue;

                        try
                        {
                            process.Kill(entireProcessTree: true);
                            AppBlocked?.Invoke(name);
                        }
                        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException or AggregateException)
                        {
                            // Нет прав (процесс запущен от администратора) или процесс уже завершился.
                        }
                    }
                }
            }
        }
    }

    /// <summary>Последняя страховка: процессы VPN-клиентов (по имени или по папке установки) не закрываем, даже если их внесли в список.</summary>
    private static bool IsProtectedProcess(Process process)
    {
        try
        {
            if (AppEntry.IsProtectedName(process.ProcessName))
                return true;

            var path = process.MainModule?.FileName;
            return path is not null && AppEntry.IsProtectedPath(path);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false; // путь прочитать не удалось — имя уже проверено
        }
    }
}
