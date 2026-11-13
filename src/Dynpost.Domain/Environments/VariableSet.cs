using Dynpost.Domain.Requests;

namespace Dynpost.Domain.Environments;

/// <summary>The values of an environment's variables, with references between them expanded.</summary>
/// <param name="Values">Each variable's final value, by name.</param>
/// <param name="Cycle">
/// Names of variables that refer to each other in a loop (and so have no value), sorted; empty
/// when there is none.
/// </param>
public sealed record ResolvedVariables(IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Cycle);

/// <summary>Expands variables whose values use other variables, e.g. <c>baseUrl: https://{{host}}</c>.</summary>
public static class VariableSet
{
    /// <summary>
    /// Resolves the values of <paramref name="variables"/>. A reference to a variable that isn't
    /// in the set is left as written, so it is reported where the value is used. Variables that
    /// refer to each other in a loop are left out of <see cref="ResolvedVariables.Values"/> and
    /// listed in <see cref="ResolvedVariables.Cycle"/>. When a name repeats, the first one wins.
    /// </summary>
    public static ResolvedVariables Resolve(IEnumerable<EnvironmentVariable> variables)
    {
        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            raw.TryAdd(variable.Name, variable.Value);
        }

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var cycle = new SortedSet<string>(StringComparer.Ordinal);
        var inProgress = new HashSet<string>(StringComparer.Ordinal);

        bool TryResolve(string name, out string value)
        {
            if (resolved.TryGetValue(name, out value!))
            {
                return true;
            }

            if (cycle.Contains(name) || !inProgress.Add(name))
            {
                cycle.Add(name);
                value = string.Empty;
                return false;
            }

            var ok = true;
            value = VariableTemplate.Replace(raw[name], reference =>
            {
                if (!raw.ContainsKey(reference))
                {
                    return null;
                }

                if (TryResolve(reference, out var inner))
                {
                    return inner;
                }

                ok = false;
                return null;
            });
            inProgress.Remove(name);

            if (!ok)
            {
                cycle.Add(name);
                return false;
            }

            resolved[name] = value;
            return true;
        }

        foreach (var name in raw.Keys)
        {
            TryResolve(name, out _);
        }

        // Variables that only use a variable from a loop aren't part of the loop themselves.
        var looping = cycle.Where(name => ReachesItself(name, raw)).ToList();
        return new ResolvedVariables(resolved, looping);
    }

    public static string CycleMessage(IReadOnlyList<string> cycle) =>
        cycle.Count == 1
            ? $"The variable '{cycle[0]}' refers to itself."
            : $"The variables {string.Join(", ", cycle.Select(name => $"'{name}'"))} refer to each other in a loop.";

    private static bool ReachesItself(string start, Dictionary<string, string> raw)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(VariableTemplate.References(raw[start]));
        while (pending.TryPop(out var name))
        {
            if (name == start)
            {
                return true;
            }

            if (raw.TryGetValue(name, out var value) && visited.Add(name))
            {
                foreach (var reference in VariableTemplate.References(value))
                {
                    pending.Push(reference);
                }
            }
        }

        return false;
    }
}
