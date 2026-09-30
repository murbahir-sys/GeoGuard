using System.Diagnostics;
using System.Net.NetworkInformation;

namespace GeoGuard;

/// <summary>
/// Применяет план файрвола в фоновом потоке: запросы схлопываются (важно только последнее требование),
/// сбой не роняет приложение, а повторяется через паузу. Медленный COM-вызов не задерживает закрытие приложений.
/// Состав сетевых адаптеров меняется и без запросов от движка (запуск WSL/Docker, Wi-Fi), поэтому план
/// перепроверяется по таймеру; лишняя работа — только сравнение отпечатка плана.
/// </summary>
public sealed class FirewallSync : IDisposable
{
    private readonly IFirewall _firewall;
    private readonly Func<IReadOnlyList<AdapterInfo>> _adapters;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private readonly Task _worker;

    private (AppSettings Settings, bool Blocked)? _request;
    private string? _appliedFingerprint;
    private string? _note;

    /// <summary>Состояние файрвола для показа пользователю (null — файрвол не используется). Вызывается из фонового потока.</summary>
    public event Action<string?>? NoteChanged;

    public FirewallSync(
        IFirewall firewall,
        Func<IReadOnlyList<AdapterInfo>>? adapters = null,
        TimeSpan? retryDelay = null,
        TimeSpan? pollInterval = null)
    {
        _firewall = firewall;
        _adapters = adapters ?? FirewallPlanner.SystemAdapters;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        _worker = Task.Run(RunAsync);
    }

    public string? Note
    {
        get { lock (_lock) return _note; }
    }

    public void Request(AppSettings settings, bool appsBlocked)
    {
        lock (_lock)
            _request = (settings, appsBlocked);
        Signal();
    }

    /// <summary>Останавливает фоновую работу и удаляет все правила (выход пользователя из приложения).</summary>
    public void Shutdown()
    {
        StopWorker();
        if (!_firewall.IsAvailable)
            return;

        try
        {
            _firewall.RemoveAll();
        }
        catch (FirewallException)
        {
            // Удалить не вышло — правила останутся; вручную: GeoGuard.exe --remove-rules.
        }
    }

    public void Dispose() => StopWorker();

    private void StopWorker()
    {
        _cts.Cancel();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }
    }

    private void Signal()
    {
        try
        {
            _signal.Release();
        }
        catch (Exception ex) when (ex is SemaphoreFullException or ObjectDisposedException)
        {
            // Запрос уже ожидает обработки.
        }
    }

    private async Task RunAsync()
    {
        var ct = _cts.Token;
        long lastFailure = 0;
        try
        {
            while (true)
            {
                var signaled = await _signal.WaitAsync(_pollInterval, ct);

                (AppSettings Settings, bool Blocked)? request;
                lock (_lock)
                    request = _request;
                if (request is null)
                    continue;

                // После сбоя не долбим API: без нового требования повтор не чаще, чем раз в _retryDelay.
                if (!signaled && lastFailure != 0 && Stopwatch.GetElapsedTime(lastFailure) < _retryDelay)
                    continue;

                lastFailure = TryApply(request.Value) ? 0 : Stopwatch.GetTimestamp();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool TryApply((AppSettings Settings, bool Blocked) request)
    {
        var (settings, blocked) = request;
        var vpnChosen = VpnMatch.Patterns(settings.VpnAdapterMatch).Length > 0;
        var wanted = settings.UseFirewall && settings.AllowedCountry.Length > 0 && vpnChosen && settings.RestrictedApps.Count > 0;

        if (wanted && !_firewall.IsAvailable)
        {
            SetNote($"Файрвол выключен: {_firewall.UnavailableReason}. Работает только закрытие приложений.");
            return true;
        }

        try
        {
            var adapters = _adapters();
            var plan = FirewallPlanner.Plan(settings, blocked, adapters);
            var fingerprint = plan.Fingerprint;
            if (fingerprint != _appliedFingerprint)
            {
                // Первый запуск с пустым планом тоже нужно применить: уберёт правила, оставшиеся от прошлого раза.
                if (_firewall.IsAvailable)
                    _firewall.Apply(plan);
                _appliedFingerprint = fingerprint;
            }

            if (settings.AllowedCountry.Length > 0 && !vpnChosen)
                SetNote("VPN-адаптер не выбран (вкладка «Сеть»): файрвол не работает, отключение VPN замечается медленнее.");
            else if (wanted && !adapters.Any(a => VpnMatch.IsMatch(a, settings.VpnAdapterMatch)))
                SetNote($"{Describe(plan, blocked)}. VPN-адаптер «{settings.VpnAdapterMatch}» не найден — проверьте вкладку «Сеть».");
            else
                SetNote(wanted ? Describe(plan, blocked) : null);
            return true;
        }
        catch (Exception ex) when (ex is FirewallException or NetworkInformationException)
        {
            SetNote(ex.Message);
            return false;
        }
    }

    private static string Describe(FirewallPlan plan, bool blocked)
    {
        var text = plan.Rules.Count == 0
            ? "Файрвол: правил нет"
            : blocked
                ? $"Файрвол: сеть закрыта приложениям ({plan.Rules.Count})"
                : $"Файрвол: вне VPN закрыто приложениям ({plan.Rules.Count})";

        return plan.Unresolved.Count == 0
            ? text
            : $"{text}. Без пути к exe (только закрытие): {string.Join(", ", plan.Unresolved)}";
    }

    private void SetNote(string? note)
    {
        lock (_lock)
        {
            if (_note == note)
                return;
            _note = note;
        }

        NoteChanged?.Invoke(note);
    }
}
