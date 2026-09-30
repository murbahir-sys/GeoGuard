using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace GeoGuard;

/// <summary>
/// Названия стран на языке интерфейса Windows («Германия», а не «Deutschland»).
/// .NET отдаёт название на языке самой страны, поэтому спрашиваем у системы.
/// </summary>
internal static class CountryNames
{
    private const int GeoFriendlyName = 8; // GEO_FRIENDLYNAME

    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetGeoInfoEx")]
    private static extern int GetGeoInfoEx(string location, int geoType, StringBuilder? geoData, int geoDataCount, ushort language);

    /// <returns>Название по двухбуквенному коду ISO 3166-1 или null, если система его не знает.</returns>
    public static string? Localized(string? isoCode)
    {
        if (string.IsNullOrEmpty(isoCode) || isoCode.Length != 2)
            return null;

        return Cache.GetOrAdd(isoCode, code =>
        {
            try
            {
                var length = GetGeoInfoEx(code, GeoFriendlyName, null, 0, 0);
                if (length <= 0)
                    return null;

                var buffer = new StringBuilder(length);
                return GetGeoInfoEx(code, GeoFriendlyName, buffer, length, 0) > 0 ? buffer.ToString() : null;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return null;
            }
        });
    }

    /// <summary>Название для списка: системное, иначе английское из .NET.</summary>
    public static string For(string isoCode, RegionInfo? region = null) =>
        Localized(isoCode) ?? region?.EnglishName ?? isoCode;
}
