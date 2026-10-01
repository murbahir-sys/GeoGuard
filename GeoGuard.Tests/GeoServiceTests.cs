using System.Net;
using System.Text;
using GeoGuard;

// Проверки службы геолокации без сети: подставные ответы сервисов и подставное время.
internal static partial class Program
{
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Отвечает за сервисы по имени хоста; считает запросы.</summary>
    private sealed class FakeHttp : HttpMessageHandler
    {
        public readonly Dictionary<string, int> Requests = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Func<HttpResponseMessage>> Responses = new(StringComparer.OrdinalIgnoreCase);

        public int Count(string host) => Requests.TryGetValue(host, out var n) ? n : 0;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri!.Host;
            lock (Requests)
                Requests[host] = Count(host) + 1;
            return Task.FromResult(Responses.TryGetValue(host, out var make)
                ? make()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        public static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        public static HttpResponseMessage TooMany(TimeSpan? retryAfter = null)
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if (retryAfter is { } delay)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
            return response;
        }
    }

    private static FakeHttp AllAnswerDe()
    {
        var http = new FakeHttp();
        http.Responses["ipwho.is"] = () => FakeHttp.Json("""{"success":true,"ip":"203.0.113.1","country":"Germany","country_code":"DE"}""");
        http.Responses["api.country.is"] = () => FakeHttp.Json("""{"ip":"203.0.113.1","country":"DE"}""");
        http.Responses["get.geojs.io"] = () => FakeHttp.Json("""{"country":"DE","country_3":"DEU","ip":"203.0.113.1","name":"Germany"}""");
        return http;
    }

    private static async Task GeoServiceRateLimits()
    {
        Console.WriteLine("# Служба геолокации бережёт лимиты сервисов (без сети)");
        var time = new ManualTime(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        // 1. ipwho.is — не чаще раза в 2 минуты, остальные — каждый раз.
        var http = AllAnswerDe();
        using (var geo = new GeoLocationService(GeoLocationService.DefaultProviders, http, time))
        {
            var first = await geo.LookupAsync(CancellationToken.None);
            Check("первая проверка: ответили все три сервиса и все разобраны", first.Count == 3 && first.All(r => r.CountryCode == "DE"));

            for (var i = 0; i < 3; i++)
            {
                time.Now += TimeSpan.FromSeconds(30);
                await geo.LookupAsync(CancellationToken.None);
            }

            Check("за 1,5 минуты ipwho.is опрошен 1 раз, остальные — 4 раза",
                http.Count("ipwho.is") == 1 && http.Count("api.country.is") == 4 && http.Count("get.geojs.io") == 4);

            time.Now += TimeSpan.FromSeconds(30); // ровно 2 минуты с первого запроса
            var later = await geo.LookupAsync(CancellationToken.None);
            Check("через 2 минуты ipwho.is опрошен снова", http.Count("ipwho.is") == 2 && later.Count == 3);

            // Сутки при проверке раз в 30 секунд (2880 проверок): к ipwho.is — не больше 720 запросов.
            var before = http.Count("ipwho.is");
            for (var i = 0; i < 2880; i++)
            {
                time.Now += TimeSpan.FromSeconds(30);
                await geo.LookupAsync(CancellationToken.None);
            }

            var perDay = http.Count("ipwho.is") - before;
            Console.WriteLine($"    за сутки к ipwho.is: {perDay} запросов (лимит бесплатного режима — 1000)");
            Check("за сутки к ipwho.is не больше 720 запросов", perDay <= 720);
        }

        // 2. Ответ 429 — сервис отдыхает 15 минут.
        http = AllAnswerDe();
        http.Responses["api.country.is"] = () => FakeHttp.TooMany();
        using (var geo = new GeoLocationService(GeoLocationService.DefaultProviders, http, time))
        {
            await geo.LookupAsync(CancellationToken.None);
            http.Responses["api.country.is"] = () => FakeHttp.Json("""{"ip":"203.0.113.1","country":"DE"}""");
            for (var i = 0; i < 29; i++) // 14,5 минуты
            {
                time.Now += TimeSpan.FromSeconds(30);
                await geo.LookupAsync(CancellationToken.None);
            }

            Check("после 429 сервис не опрашивается 15 минут", http.Count("api.country.is") == 1);
            time.Now += TimeSpan.FromSeconds(30);
            var answers = await geo.LookupAsync(CancellationToken.None);
            Check("через 15 минут сервис снова опрашивается и отвечает", http.Count("api.country.is") == 2 && answers.Count >= 2);
        }

        // 3. Retry-After из ответа соблюдается (но не дольше часа).
        http = AllAnswerDe();
        http.Responses["get.geojs.io"] = () => FakeHttp.TooMany(TimeSpan.FromSeconds(60));
        using (var geo = new GeoLocationService(GeoLocationService.DefaultProviders, http, time))
        {
            await geo.LookupAsync(CancellationToken.None);
            time.Now += TimeSpan.FromSeconds(30);
            await geo.LookupAsync(CancellationToken.None);
            Check("Retry-After 60 с: через 30 с сервис не опрашивается", http.Count("get.geojs.io") == 1);
            time.Now += TimeSpan.FromSeconds(31);
            await geo.LookupAsync(CancellationToken.None);
            Check("…а через 61 с — снова опрашивается", http.Count("get.geojs.io") == 2);
        }

        http = AllAnswerDe();
        http.Responses["get.geojs.io"] = () => FakeHttp.TooMany(TimeSpan.FromDays(2));
        using (var geo = new GeoLocationService(GeoLocationService.DefaultProviders, http, time))
        {
            await geo.LookupAsync(CancellationToken.None);
            time.Now += TimeSpan.FromMinutes(61);
            await geo.LookupAsync(CancellationToken.None);
            Check("слишком долгий Retry-After ограничен часом", http.Count("get.geojs.io") == 2);
        }

        // 4. Остальные «отдыхают» — сервис с интервалом опрашивается без ожидания.
        http = AllAnswerDe();
        http.Responses["api.country.is"] = () => FakeHttp.TooMany();
        http.Responses["get.geojs.io"] = () => FakeHttp.TooMany();
        using (var geo = new GeoLocationService(GeoLocationService.DefaultProviders, http, time))
        {
            await geo.LookupAsync(CancellationToken.None); // оба получили 429, ipwho.is ответил
            time.Now += TimeSpan.FromSeconds(30);
            var answers = await geo.LookupAsync(CancellationToken.None);
            Check("если другие недоступны, ipwho.is опрашивается раньше 2 минут, и проверка не остаётся без ответа",
                http.Count("ipwho.is") == 2 && answers.Count == 1 && answers[0].CountryCode == "DE");
        }
    }
}
