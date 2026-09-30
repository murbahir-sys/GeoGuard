using System.Text.Json;

namespace GeoGuard;

public sealed record GeoResult(string CountryCode, string? CountryName, string? Ip);

public interface IGeoLookup : IDisposable
{
    /// <returns>Ответы всех сервисов, которые ответили; пустой список, если не ответил ни один.</returns>
    Task<IReadOnlyList<GeoResult>> LookupAsync(CancellationToken ct);
}

/// <summary>
/// Определяет страну по внешнему IP. Все сервисы опрашиваются параллельно: решение принимает движок
/// и разрешает приложения, только если страну подтвердили все ответившие сервисы.
/// </summary>
public sealed class GeoLocationService : IGeoLookup
{
    private sealed record Provider(string Url, Func<JsonElement, GeoResult?> Parse);

    // Только сервисы, чьи условия разрешают бесплатное использование, в том числе коммерческое, без ключа.
    private static readonly Provider[] Providers =
    [
        new("https://ipwho.is/?fields=success,ip,country,country_code",
            root => root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True
                ? Read(root, "country_code", "country", "ip")
                : null),
        new("https://api.country.is/",
            root => Read(root, "country", null, "ip")),
        new("https://get.geojs.io/v1/ip/country.json",
            root => Read(root, "country", "name", "ip")),
    ];

    private readonly HttpClient _http;

    public GeoLocationService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GeoGuard/1.0");
        // Без повторного использования соединений: после включения VPN старое соединение
        // могло бы продолжать идти по прежнему маршруту и показывать прежний IP.
        _http.DefaultRequestHeaders.ConnectionClose = true;
    }

    public async Task<IReadOnlyList<GeoResult>> LookupAsync(CancellationToken ct)
    {
        var answers = await Task.WhenAll(Providers.Select(p => QueryAsync(p, ct)));
        ct.ThrowIfCancellationRequested();
        return answers.OfType<GeoResult>().ToList();
    }

    public void Dispose() => _http.Dispose();

    private async Task<GeoResult?> QueryAsync(Provider provider, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(provider.Url, ct);
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
