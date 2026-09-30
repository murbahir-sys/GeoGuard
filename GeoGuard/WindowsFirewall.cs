using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace GeoGuard;

public sealed class FirewallException(string message, Exception? inner = null) : Exception(message, inner);

public interface IFirewall
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }

    /// <summary>Приводит правила к плану. Бросает <see cref="FirewallException"/> при ошибке.</summary>
    void Apply(FirewallPlan plan);

    /// <summary>Удаляет все правила, созданные этим приложением.</summary>
    void RemoveAll();
}

/// <summary>Описание правила, прочитанного из файрвола (для самопроверки).</summary>
public sealed record FirewallRuleInfo(string Name, string Application, string Interfaces, int Action, int Direction, bool Enabled);

/// <summary>
/// Правила Брандмауэра Windows через COM (HNetCfg.FwPolicy2). Нужны права администратора.
/// Все правила приложения имеют общий префикс имени — только их GeoGuard создаёт и удаляет.
/// При смене правила сначала добавляется новое и лишь потом удаляется старое, чтобы не было окна без защиты.
/// </summary>
public sealed class WindowsFirewall(string rulePrefix = WindowsFirewall.DefaultPrefix) : IFirewall
{
    public const string DefaultPrefix = "GeoGuard: ";

    private const int DirectionOut = 2;      // NET_FW_RULE_DIR_OUT
    private const int ActionBlock = 0;       // NET_FW_ACTION_BLOCK
    private const int AllProfiles = 0x7FFFFFFF; // NET_FW_PROFILE2_ALL

    private readonly object _lock = new();
    private readonly string _session = Guid.NewGuid().ToString("N")[..6];
    private readonly Dictionary<string, (string Fingerprint, string RuleName)> _applied = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _stale = new();
    private bool _initialized;
    private int _generation;

    public bool IsAvailable => Elevation.IsElevated && Type.GetTypeFromProgID("HNetCfg.FwPolicy2") is not null;

    public string? UnavailableReason => !Elevation.IsElevated ? "нет прав администратора" : IsAvailable ? null : "API файрвола недоступен";

    public void Apply(FirewallPlan plan)
    {
        Guard(() =>
        {
            lock (_lock)
            {
                dynamic rules = CreatePolicy().Rules;
                if (!_initialized)
                {
                    // Правила, оставшиеся от прошлого запуска (или сбоя), удаляем после того, как поставим свежие.
                    _stale.AddRange(OwnedRuleNames(rules));
                    _initialized = true;
                }

                var created = new List<string>();
                var next = new Dictionary<string, (string Fingerprint, string RuleName)>(StringComparer.OrdinalIgnoreCase);
                var toRemove = new List<string>(_stale);
                try
                {
                    foreach (var spec in plan.Rules)
                    {
                        _applied.TryGetValue(spec.Path, out var current);
                        if (current.RuleName is not null && current.Fingerprint == spec.Fingerprint)
                        {
                            next[spec.Path] = current;
                            continue;
                        }

                        var name = $"{rulePrefix}{System.IO.Path.GetFileName(spec.Path)} [{_session}-{++_generation}]";
                        rules.Add(CreateRuleObject(name, spec));
                        created.Add(name);
                        next[spec.Path] = (spec.Fingerprint, name);
                        if (current.RuleName is not null)
                            toRemove.Add(current.RuleName);
                    }
                }
                catch
                {
                    // Уже созданные в этом проходе правила запомним — следующий успешный проход их уберёт.
                    _stale.AddRange(created);
                    throw;
                }

                foreach (var (path, applied) in _applied)
                {
                    if (!next.ContainsKey(path))
                        toRemove.Add(applied.RuleName);
                }

                _applied.Clear();
                foreach (var (path, applied) in next)
                    _applied[path] = applied;

                _stale.Clear();
                for (var i = 0; i < toRemove.Count; i++)
                {
                    try
                    {
                        rules.Remove(toRemove[i]);
                    }
                    catch
                    {
                        // Не удалось убрать — запомним оставшееся, чтобы следующий проход дочистил.
                        _stale.AddRange(toRemove.Skip(i));
                        throw;
                    }
                }
            }
        });
    }

    public void RemoveAll()
    {
        Guard(() =>
        {
            lock (_lock)
            {
                dynamic rules = CreatePolicy().Rules;
                foreach (var name in OwnedRuleNames(rules))
                    rules.Remove(name);

                _applied.Clear();
                _stale.Clear();
                _initialized = true;
            }
        });
    }

    /// <summary>Число правил с нашим префиксом (только чтение — прав администратора не требует).</summary>
    public int CountOwnedRules()
    {
        var count = 0;
        Guard(() => count = OwnedRuleNames(CreatePolicy().Rules).Count);
        return count;
    }

    /// <summary>Правила с нашим префиксом в том виде, как их видит файрвол (только чтение).</summary>
    public IReadOnlyList<FirewallRuleInfo> ReadOwnedRules()
    {
        var result = new List<FirewallRuleInfo>();
        Guard(() =>
        {
            dynamic rules = CreatePolicy().Rules;
            foreach (dynamic rule in rules)
            {
                string? name = rule.Name;
                if (name is null || !name.StartsWith(rulePrefix, StringComparison.Ordinal))
                    continue;

                object? interfaces = rule.Interfaces;
                result.Add(new FirewallRuleInfo(
                    name,
                    (string?)rule.ApplicationName ?? "",
                    interfaces is object[] names ? string.Join("|", names) : "*",
                    (int)rule.Action,
                    (int)rule.Direction,
                    (bool)rule.Enabled));
            }
        });
        return result;
    }

    /// <summary>Создаёт объект правила, не добавляя его в систему (для проверки свойств без прав администратора).</summary>
    public static dynamic CreateRuleObject(string name, FirewallRuleSpec spec)
    {
        dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!)!;
        rule.Name = name;
        rule.Description = "Создано GeoGuard: закрывает сеть приложению вне VPN или пока страна не подтверждена.";
        rule.ApplicationName = spec.Path;
        rule.Direction = DirectionOut;
        rule.Action = ActionBlock;
        rule.Profiles = AllProfiles;
        rule.Grouping = "GeoGuard";
        if (spec.Interfaces is not null)
            rule.Interfaces = spec.Interfaces.Cast<object>().ToArray();
        rule.Enabled = true;
        return rule;
    }

    private static dynamic CreatePolicy() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;

    private List<string> OwnedRuleNames(dynamic rules)
    {
        var names = new List<string>();
        foreach (dynamic rule in rules)
        {
            string? name = rule.Name;
            if (name is not null && name.StartsWith(rulePrefix, StringComparison.Ordinal))
                names.Add(name);
        }

        return names;
    }

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or UnauthorizedAccessException
                                       or InvalidCastException or ArgumentException or TypeLoadException or FileNotFoundException)
        {
            throw new FirewallException($"Ошибка файрвола: {ex.Message}", ex);
        }
    }
}
