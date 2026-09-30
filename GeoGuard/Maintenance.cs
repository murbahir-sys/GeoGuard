using System.Text;

namespace GeoGuard;

/// <summary>Служебные команды: GeoGuard.exe --remove-rules и GeoGuard.exe --selftest-firewall. Обе требуют прав администратора.</summary>
internal static class Maintenance
{
    private const string Title = "GeoGuard";

    /// <summary>Включает или выключает автозапуск (вызывается установщиком). Код 0 — успех.</summary>
    public static int SetAutostart(bool enable) => AutoStart.Apply(enable) ? 0 : 1;

    /// <summary>Удаляет все правила файрвола GeoGuard и автозапуск (на случай удаления программы или сбоя).</summary>
    public static int RemoveEverything(bool silent = false)
    {
        var problems = new List<string>();

        try
        {
            new WindowsFirewall().RemoveAll();
        }
        catch (FirewallException ex)
        {
            problems.Add(ex.Message);
        }

        if (!AutoStart.Remove())
            problems.Add("Не удалось удалить автозапуск.");

        MessageBox.Show(
            problems.Count == 0 ? "Правила файрвола GeoGuard и автозапуск удалены." : string.Join("\n", problems),
            Title,
            MessageBoxButtons.OK,
            problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        return problems.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Проверяет файрвол на реальной системе: создаёт правило для несуществующего exe, читает его обратно, заменяет, удаляет.
    /// Правила имеют отдельный префикс и не затрагивают рабочие правила GeoGuard и любые реальные программы.
    /// </summary>
    public static int FirewallSelfTest()
    {
        var report = new StringBuilder();
        var allPassed = true;

        void Step(string title, bool passed, string details = "")
        {
            allPassed &= passed;
            report.AppendLine($"[{(passed ? "OK" : "ПРОВАЛ")}] {title}{(details.Length > 0 ? $" — {details}" : "")}");
        }

        var firewall = new WindowsFirewall("GeoGuard-selftest: ");
        try
        {
            Step("права администратора", Elevation.IsElevated);
            Step("API файрвола доступен", firewall.IsAvailable, firewall.UnavailableReason ?? "");

            var adapter = FirewallPlanner.SystemAdapters().FirstOrDefault(a => !a.IsLoopback)?.Name;
            Step("найден сетевой адаптер для проверки", adapter is not null, adapter ?? "");

            if (firewall.IsAvailable && adapter is not null)
            {
                const string dummy = @"C:\GeoGuardSelfTest\dummy.exe";
                firewall.RemoveAll();

                firewall.Apply(new FirewallPlan([new FirewallRuleSpec(dummy, [adapter])], []));
                var rules = firewall.ReadOwnedRules();
                var first = rules.FirstOrDefault();
                Step(
                    "правило для выбранного интерфейса создано и читается обратно",
                    rules.Count == 1 && first is { Action: 0, Direction: 2, Enabled: true } && first.Interfaces == adapter
                        && string.Equals(first.Application, dummy, StringComparison.OrdinalIgnoreCase),
                    first is null ? "правил нет" : $"интерфейсы: {first.Interfaces}, действие: {first.Action}, направление: {first.Direction}");

                firewall.Apply(new FirewallPlan([new FirewallRuleSpec(dummy, null)], []));
                rules = firewall.ReadOwnedRules();
                Step("правило для всех интерфейсов заменило прежнее без дублей", rules.Count == 1 && rules[0].Interfaces == "*", $"правил: {rules.Count}");

                firewall.RemoveAll();
                Step("тестовые правила удалены", firewall.CountOwnedRules() == 0);
            }
        }
        catch (FirewallException ex)
        {
            Step("исключение файрвола", false, ex.Message);
        }
        finally
        {
            try
            {
                if (firewall.IsAvailable)
                    firewall.RemoveAll();
            }
            catch (FirewallException)
            {
                report.AppendLine("Внимание: тестовые правила «GeoGuard-selftest: …» удалить не удалось — удалите вручную.");
            }
        }

        var text = report.ToString();
        try
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            File.WriteAllText(Path.Combine(SettingsStore.DirectoryPath, "selftest.txt"), text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Отчёт всё равно показываем в окне.
        }

        MessageBox.Show(text, "GeoGuard — проверка файрвола", MessageBoxButtons.OK, allPassed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        return allPassed ? 0 : 1;
    }
}
