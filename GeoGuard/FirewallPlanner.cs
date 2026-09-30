using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GeoGuard;

/// <param name="Ipv4">IPv4-адреса без link-local (169.254.x.x).</param>
/// <param name="Gateways">Шлюзы по умолчанию (IPv4 и IPv6), без «пустых» 0.0.0.0 и ::.</param>
/// <param name="IsVirtualTunnel">Тип адаптера — туннель, PPP или виртуальный (так выглядят WireGuard, Wintun, TAP и VPN-подключения Windows).</param>
public sealed record AdapterInfo(
    string Id,
    string Name,
    string Description,
    bool IsLoopback,
    bool IsUp,
    bool IsWireless,
    IReadOnlyList<string> Ipv4,
    IReadOnlyList<string> Gateways,
    bool IsVirtualTunnel = false)
{
    public bool HasGateway => Gateways.Count > 0;

    /// <summary>Похож ли адаптер на VPN — подсказка при выборе, решение остаётся за пользователем.</summary>
    public bool LooksLikeVpn => VpnDetector.LooksLikeVpn(this);

    /// <summary>
    /// Может ли адаптер вести в интернет: у него есть шлюз, либо это Wi-Fi, либо он сейчас не работает (мог бы включиться).
    /// Внутренние сети WSL, Docker, VMware, Hyper-V Default Switch работают, но шлюза не имеют — они не в счёт.
    /// </summary>
    public bool IsInternetFacing => HasGateway || IsWireless || !IsUp;
}

/// <summary>Узнаёт адаптеры, похожие на VPN: по типу (туннель, PPP, виртуальный) или по известным названиям драйверов и клиентов.</summary>
public static class VpnDetector
{
    private static readonly string[] Keywords =
    [
        "vpn", "wireguard", "wintun", "tap-windows", "tap adapter", "openvpn", "amnezia", "nordlynx", "proton", "mullvad",
        "outline", "wiresock", "anyconnect", "cisco", "fortinet", "forticlient", "globalprotect", "pangp", "sing-tun",
        "hiddify", "v2ray", "xray", "clash", "tun2socks", "tailscale", "zerotier", "tunnel",
    ];

    /// <summary>Служебные туннели Windows — не VPN.</summary>
    private static readonly string[] NotVpn = ["teredo", "6to4", "isatap", "ip-https", "kernel debug", "loopback"];

    public static bool LooksLikeVpn(AdapterInfo adapter)
    {
        var text = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
        if (adapter.IsLoopback || NotVpn.Any(text.Contains))
            return false;
        return adapter.IsVirtualTunnel || Keywords.Any(text.Contains);
    }
}

/// <summary>Сопоставление адаптера с VPN. Шаблон — часть имени или описания без учёта регистра; несколько шаблонов через «;» или «,».</summary>
public static class VpnMatch
{
    public static string[] Patterns(string? match) =>
        (match ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool IsMatch(AdapterInfo adapter, string? match) =>
        Patterns(match).Any(p =>
            adapter.Name.Contains(p, StringComparison.OrdinalIgnoreCase)
            || adapter.Description.Contains(p, StringComparison.OrdinalIgnoreCase));
}

/// <param name="Interfaces">Имена сетевых интерфейсов, на которых правило блокирует приложение; null — на всех.</param>
public sealed record FirewallRuleSpec(string Path, IReadOnlyList<string>? Interfaces)
{
    public string Fingerprint => Interfaces is null ? "*" : string.Join("|", Interfaces);
}

/// <param name="Unresolved">Записи списка, для которых не найден путь к exe: файрвол на них не действует.</param>
public sealed record FirewallPlan(IReadOnlyList<FirewallRuleSpec> Rules, IReadOnlyList<string> Unresolved)
{
    public static FirewallPlan Empty { get; } = new(Array.Empty<FirewallRuleSpec>(), Array.Empty<string>());

    public string Fingerprint => string.Join(";", Rules.Select(r => $"{r.Path}={r.Fingerprint}"));
}

/// <summary>
/// Решает, какие правила файрвола нужны. Идея «выключателя VPN» на уровне сети:
/// - страна подтверждена: приложению закрыты все интернет-интерфейсы, КРОМЕ VPN-адаптера — если VPN упадёт, трафик по обычной сети не пойдёт;
/// - страна не подтверждена или чужая: приложению закрыты вообще все интерфейсы.
/// Внутренние сети (WSL, Docker, виртуальные машины) остаются открытыми: выхода в интернет через них у приложения нет.
/// Правила блокирующие: они сильнее любых разрешающих, поэтому сам VPN и другие программы не затрагиваются.
/// </summary>
public static class FirewallPlanner
{
    public static FirewallPlan Plan(AppSettings settings, bool appsBlocked, IReadOnlyList<AdapterInfo> adapters)
    {
        // Без выбранной страны или без понятия «VPN-адаптер» защищать нечего.
        if (!settings.UseFirewall || settings.AllowedCountry.Length == 0 || VpnMatch.Patterns(settings.VpnAdapterMatch).Length == 0)
            return FirewallPlan.Empty;

        var paths = new List<string>();
        var unresolved = new List<string>();
        foreach (var entry in settings.RestrictedApps)
        {
            var path = AppEntry.ResolvePath(entry);
            if (path is null)
                unresolved.Add(entry);
            else if (!AppEntry.IsProtected(path) && !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                paths.Add(path);
        }

        paths.Sort(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string>? outsideVpn = null; // null — закрыть все интерфейсы
        if (!appsBlocked)
        {
            outsideVpn = adapters
                .Where(a => !a.IsLoopback && !VpnMatch.IsMatch(a, settings.VpnAdapterMatch) && a.IsInternetFacing)
                .Select(a => a.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Вне VPN закрывать нечего (в системе только VPN, loopback и внутренние сети).
            if (outsideVpn.Count == 0)
                return new FirewallPlan(Array.Empty<FirewallRuleSpec>(), unresolved);
        }

        return new FirewallPlan(paths.Select(p => new FirewallRuleSpec(p, outsideVpn)).ToList(), unresolved);
    }

    public static bool IsVpnAdapter(AdapterInfo adapter, string match) => VpnMatch.IsMatch(adapter, match);

    /// <summary>
    /// Адаптеры, которые файрвол принимает в правилах. .NET отдаёт и служебные фильтры NDIS
    /// («Ethernet-QoS Packet Scheduler-0000» и т. п.), и адаптеры, отданные виртуальному коммутатору Hyper-V, —
    /// без IP-привязки файрвол такие имена отвергает, поэтому оставляем только адаптеры с IPv4 или IPv6.
    /// </summary>
    public static IReadOnlyList<AdapterInfo> SystemAdapters() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.Supports(NetworkInterfaceComponent.IPv4) || n.Supports(NetworkInterfaceComponent.IPv6))
        .Select(ToAdapterInfo)
        .ToList();

    private static AdapterInfo ToAdapterInfo(NetworkInterface nic)
    {
        var properties = nic.GetIPProperties();
        return new AdapterInfo(
            nic.Id,
            nic.Name,
            nic.Description,
            nic.NetworkInterfaceType == NetworkInterfaceType.Loopback,
            nic.OperationalStatus == OperationalStatus.Up,
            nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
            properties.UnicastAddresses
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IsLinkLocal(a))
                .Select(a => a.ToString())
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList(),
            properties.GatewayAddresses
                .Select(g => g.Address)
                .Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any))
                .Select(a => a.ToString())
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList(),
            IsVirtualTunnel(nic.NetworkInterfaceType));
    }

    /// <summary>Туннель, PPP (VPN-подключения Windows) или виртуальный адаптер (IF_TYPE_PROP_VIRTUAL = 53: WireGuard, Wintun, TAP).</summary>
    private static bool IsVirtualTunnel(NetworkInterfaceType type) =>
        type is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp || (int)type == 53;

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }
}
