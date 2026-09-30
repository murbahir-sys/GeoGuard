namespace GeoGuard;

/// <summary>
/// Отпечаток того, как компьютер выходит в интернет. Меняется, только когда внешний IP мог измениться:
/// - VPN-адаптер (по имени) включился, выключился или сменил адрес;
/// - изменился адаптер, который ведёт в интернет (у него есть шлюз): Wi-Fi/кабель/точка доступа, смена сети.
/// Всё остальное игнорируется — запуск и остановка WSL, Docker, виртуальных машин, Hyper-V Default Switch:
/// у их адаптеров нет шлюза, и на внешний IP они не влияют.
/// IPv6-адреса не учитываются: временные адреса меняются сами по себе и давали бы ложные срабатывания.
/// </summary>
public static class NetworkSignature
{
    /// <param name="vpnMatch">Шаблон имени VPN-адаптера из настроек (может быть пустым).</param>
    public static string Compute(string vpnMatch) => Compute(FirewallPlanner.SystemAdapters(), vpnMatch);

    public static string Compute(IEnumerable<AdapterInfo> adapters, string vpnMatch)
    {
        var parts = adapters
            .Where(a => a.IsUp && !a.IsLoopback && (a.HasGateway || VpnMatch.IsMatch(a, vpnMatch)))
            .Select(a => $"{a.Id}|{string.Join(',', a.Ipv4)}|{string.Join(',', a.Gateways)}")
            .OrderBy(p => p, StringComparer.Ordinal);
        return string.Join(';', parts);
    }
}
