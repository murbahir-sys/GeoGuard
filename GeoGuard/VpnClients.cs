namespace GeoGuard;

/// <summary>
/// Известные VPN-клиенты. GeoGuard никогда не закрывает их процессы и не создаёт для них правил файрвола,
/// даже если их внесли в список: отключить сам VPN — ровно то, от чего программа защищает.
/// </summary>
public static class VpnClients
{
    /// <summary>Имена процессов (без .exe), которые защищены целиком. Сравнение без учёта регистра.</summary>
    private static readonly HashSet<string> ExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "wireguard", "openvpn", "openvpn-gui", "openvpnserv", "openvpnserv2", "OpenVPNConnect", "ovpnconnector",
        "Outline", "OutlineService",
        "ProtonVPN", "ProtonVPNService", "ProtonVPN.WireGuardService",
        "NordVPN", "nordvpn-service", "NordVPN.Service",
        "mullvad-vpn", "mullvad-daemon",
        "ExpressVPN", "expressvpnd", "ExpressVPN.AppService",
        "Surfshark", "Surfshark.Service",
        "Windscribe", "WindscribeService",
        "hiddify", "HiddifyCli", "v2rayN", "v2ray", "xray", "sing-box", "nekoray", "nekobox", "clash-verge", "Clash for Windows",
        "wiresock-client", "wiresock-vpn-client", "wiresock-client-service",
        "vpnui", "vpnagent", "csc_ui",                 // Cisco AnyConnect / Secure Client
        "FortiClient", "FortiTray", "FortiSSLVPNdaemon",
        "PanGPA", "PanGPS",                            // Palo Alto GlobalProtect
        "tailscale-ipn", "tailscaled", "zerotier_desktop_ui", "zerotier-one_x64",
    };

    /// <summary>Начала имён процессов: всё семейство программы (например, AmneziaVPN, AmneziaVPN-service, AmneziaWG).</summary>
    private static readonly string[] NamePrefixes = ["amnezia", "wireguard", "protonvpn", "nordvpn", "expressvpn", "windscribe", "surfshark"];

    /// <summary>Папки установки VPN-клиентов (фрагменты пути).</summary>
    private static readonly string[] Folders =
    [
        @"\AmneziaVPN\", @"\WireGuard\", @"\OpenVPN\", @"\OpenVPN Connect\", @"\Outline\",
        @"\Proton\VPN\", @"\Proton Technologies\ProtonVPN\", @"\NordVPN\", @"\Mullvad VPN\", @"\ExpressVPN\",
        @"\Surfshark\", @"\Windscribe\", @"\WireSock VPN Client\", @"\Hiddify\", @"\v2rayN\",
        @"\Cisco AnyConnect Secure Mobility Client\", @"\Cisco Secure Client\", @"\FortiClient\", @"\GlobalProtect\",
        @"\Tailscale\", @"\ZeroTier\",
    ];

    public static bool IsClientName(string processName) =>
        ExactNames.Contains(processName)
        || NamePrefixes.Any(prefix => processName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static bool IsClientPath(string path) =>
        Folders.Any(folder => path.Contains(folder, StringComparison.OrdinalIgnoreCase));
}
