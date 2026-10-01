using System.Net;
using System.Text.Json;

namespace GeoGuard;

public sealed record GeoResult(string CountryCode, string? CountryName, string? Ip);

public interface IGeoLookup : IDisposable
{
    /// <returns>Ответы всех сервисов, которые ответили; пустой список, если не ответил ни один.</returns>
    Task<IReadOnlyList<GeoResult>> LookupAsync(CancellationToken ct);
}

/// <summary>
/// Определяет страну по внешнему IP. Сервисы опрашиваются параллельно: решение принимает движок
/// и разрешает приложения, только если страну подтвердили все ответившие сервисы.
/// </summary>
/// <remarks>
/// Бережём лимиты бесплатных сервисов:
/// - к сервису с суточным лимитом (<see cref="Provider.MinInterval"/>) обращаемся не чаще заданного интервала;
///   пропущенный сервис просто не участвует в этой проверке — решают остальные;
/// - сервис, ответивший 429 («слишком много запросов»), не трогаем <see cref="RateLimitBackoff"/>
///   или столько, сколько он сам попросил в заголовке Retry-After (но не больше <see cref="MaxBackoff"/>);
/// - если остальные сервисы недоступны, сервис с интервалом опрашивается без ожидания, чтобы проверка не осталась без ответов.
/// </remarks>
public sealed class GeoLocationService : IGeoLookup
{
    /// <param name="MinInterval">Минимальный промежуток между запросами к сервису; <see cref="TimeSpan.Zero"/> — без ограничения.</param>
    internal sealed record Provider(string Name, string Url, Func<JsonElement, GeoResult?> Parse, TimeSpan MinInterval);

    internal static readonly TimeSpan RateLimitBackoff = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    // Только сервисы, чьи условия разрешают бесплатное использование, в том числе коммерческое, без ключа.
    internal static readonly Provider[] DefaultProviders =
    [
        // Бесплатно 1000 запросов в сутки: раз в 2 минуты — не больше 720 даже при круглосуточной работе.
        new("ipwho.is", "https://ipwho.is/?fields=success,ip,country,country_code",
            root => root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True
                ? Read(root, "country_code", "country", "ip")
                : null,
            TimeSpan.FromMinutes(2)),
        new("api.country.is", "https://api.country.is/",
            root => Read(root, "country", null, "ip"),
            TimeSpan.Zero),
        new("get.geojs.io", "https://get.geojs.io/v1/ip/country.json",
            root => Read(root, "country", "name", "ip"),
            TimeSpan.Zero),
    ];

    private sealed class ProviderState
    {
        public DateTimeOffset? LastRequest;
        public DateTimeOffset BlockedUntil = DateTimeOffset.MinValue;
    }

    private readonly Provider[] _providers;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly Dictionary<Provider, ProviderState> _state;
    private readonly object _lock = new();

    public GeoLocationService()
        : this(DefaultProviders, new HttpClientHandler(), TimeProvider.System)
    {
    }

    internal GeoLocationService(Provider[] providers, HttpMessageHandler handler, TimeProvider time)
    {
        _providers = providers;
        _time = time;
        _state = providers.ToDictionary(p => p, _ => new ProviderState());
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GeoGuard/1.0");
        // Без повторного использования соединений: после включения VPN старое соединение
        // могло бы продолжать идти по прежнему маршруту и показывать прежний IP.
        _http.DefaultRequestHeaders.ConnectionClose = true;
    }

    public async Task<IReadOnlyList<GeoResult>> LookupAsync(CancellationToken ct)
    {
        var due = SelectDue();
        var answers = await Task.WhenAll(due.Select(p => QueryAsync(p, ct)));
        ct.ThrowIfCancellationRequested();
        return answers.OfType<GeoResult>().ToList();
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Сервисы, к которым можно обратиться сейчас; отмечает время запроса.</summary>
    private List<Provider> SelectDue()
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            var available = _providers.Where(p => now >= _state[p].BlockedUntil).ToList();
            var due = available.Where(p => p.MinInterval <= TimeSpan.Zero
                                           || _state[p].LastRequest is not { } last
                                           || now - last >= p.MinInterval).ToList();

            // Без интервала некому ответить (остальные «отдыхают» после 429) — тогда не ждём и сервисы с интервалом.
            if (!due.Any(p => p.MinInterval <= TimeSpan.Zero))
                due = available;

            foreach (var provider in due)
                _state[provider].LastRequest = now;
            return due;
        }
    }

    private async Task<GeoResult?> QueryAsync(Provider provider, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(provider.Url, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Backoff(provider, response.Headers.RetryAfter);
                return null;
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return provider.Parse(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            // Сервис недоступен, не успел ответить или ответил неожиданно — считаем, что он не ответил.
            return null;
        }
    }

    private void Backoff(Provider provider, System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter)
    {
        var now = _time.GetUtcNow();
        var wait = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - now : RateLimitBackoff);
        if (wait <= TimeSpan.Zero)
            wait = RateLimitBackoff;
        if (wait > MaxBackoff)
            wait = MaxBackoff;

        lock (_lock)
            _state[provider].BlockedUntil = now + wait;
    }

    private static GeoResult? Read(JsonElement root, string codeProperty, string? nameProperty, string? ipProperty)
    {
        var code = GetString(root, codeProperty);
        if (code is not { Length: 2 })
            return null;

        return new GeoResult(
            code.ToUpperInvariant(),
            nameProperty is null ? null : GetString(root, nameProperty),
            ipProperty is null ? null : GetString(root, ipProperty));
    }

    private static string? GetString(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
