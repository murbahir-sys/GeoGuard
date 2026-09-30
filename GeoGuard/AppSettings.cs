namespace GeoGuard;

public sealed class AppSettings
{
    public const int MinIntervalSeconds = 5;
    public const int MaxIntervalSeconds = 3600;

    /// <summary>ISO-код страны (например, "RU"), в которой приложениям из списка разрешено работать. Пусто — ограничение выключено.</summary>
    public string AllowedCountry { get; set; } = "";

    /// <summary>
    /// Приложения, которые работают только в AllowedCountry. Запись — имя процесса ("notepad") или полный путь к exe;
    /// файрвол действует только для записей, у которых известен путь.
    /// </summary>
    public List<string> RestrictedApps { get; set; } = new();

    public int CheckIntervalSeconds { get; set; } = 30;

    public bool StartWithWindows { get; set; } = true;

    public bool ShowNotifications { get; set; } = true;

    /// <summary>
    /// Имя (или часть имени/описания) сетевого адаптера VPN, без учёта регистра; несколько — через «;».
    /// Через него приложениям разрешён выход в сеть. По умолчанию не задан — пользователь выбирает адаптер своего VPN.
    /// </summary>
    public string VpnAdapterMatch { get; set; } = "";

    /// <summary>Закрывать приложениям сеть вне VPN правилами файрвола (нужны права администратора).</summary>
    public bool UseFirewall { get; set; } = true;

    /// <summary>Приводит значения к допустимым. Возвращает этот же объект.</summary>
    public AppSettings Normalize()
    {
        var country = (AllowedCountry ?? "").Trim().ToUpperInvariant();
        AllowedCountry = country.Length == 2 && country.All(char.IsAsciiLetter) ? country : "";

        RestrictedApps = (RestrictedApps ?? new List<string>())
            .Select(AppEntry.Normalize)
            .Where(e => e.Length > 0 && !AppEntry.IsProtected(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        VpnAdapterMatch = (VpnAdapterMatch ?? "").Trim();
        CheckIntervalSeconds = Math.Clamp(CheckIntervalSeconds, MinIntervalSeconds, MaxIntervalSeconds);
        return this;
    }

    /// <summary>Имена процессов для закрытия: без повторов и без защищённых.</summary>
    public string[] ProcessNames() => RestrictedApps
        .Select(AppEntry.ProcessName)
        .Where(n => n.Length > 0 && !AppEntry.IsProtectedName(n))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public AppSettings Clone() => new()
    {
        AllowedCountry = AllowedCountry,
        RestrictedApps = new List<string>(RestrictedApps),
        CheckIntervalSeconds = CheckIntervalSeconds,
        StartWithWindows = StartWithWindows,
        ShowNotifications = ShowNotifications,
        VpnAdapterMatch = VpnAdapterMatch,
        UseFirewall = UseFirewall,
    };
}
