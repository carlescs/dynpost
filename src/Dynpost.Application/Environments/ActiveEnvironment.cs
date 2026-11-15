using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Environments;
using Dynpost.Domain.Requests;

namespace Dynpost.Application.Environments;

/// <summary>The variables requests are sent with.</summary>
/// <param name="EnvironmentName">The selected environment's name, or <c>null</c> when none is selected.</param>
/// <param name="Values">The variables' resolved values, by name.</param>
/// <param name="UnsetSecrets">
/// Secret variables the environment declares but that have no value on this computer (for
/// example after cloning the workspace). They are not in <see cref="Values"/>.
/// </param>
/// <param name="EnvironmentId">The selected environment's file name, or <c>null</c> when none is selected.</param>
/// <param name="SecretValues">The resolved values of the secret variables, to hide them where requests are shown.</param>
/// <param name="CookieJar">Whether the selected environment keeps cookies between requests.</param>
public sealed record ActiveVariables(
    string? EnvironmentName,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlySet<string> UnsetSecrets,
    string? EnvironmentId = null,
    IReadOnlyList<string>? SecretValues = null,
    bool CookieJar = false)
{
    public static ActiveVariables None { get; } =
        new(null, new Dictionary<string, string>(), new HashSet<string>());

    /// <summary>
    /// The names in <see cref="Values"/> that come from the workspace-global variables (the
    /// environment doesn't define them), so a folder's variable of the same name wins over them.
    /// </summary>
    public IReadOnlySet<string> Globals { get; init; } = new HashSet<string>();

    /// <summary>Explains where the values of undefined variables should come from; <c>null</c> when none are missing.</summary>
    public string? MissingVariablesHint(IReadOnlyList<string> missing)
    {
        if (missing.Count == 0)
        {
            return null;
        }

        var builtIn = missing.Where(DynamicVariables.IsDynamic).ToList();
        var builtInHint = builtIn.Count == 0
            ? null
            : $"Names starting with '$' are built-in variables; the supported ones are {string.Join(", ", DynamicVariables.All.Select(v => v.Name))} ({{{{$randomInt min max}}}} takes two whole numbers).";
        if (builtIn.Count == missing.Count)
        {
            return builtInHint;
        }

        string hint;
        if (EnvironmentName is null)
        {
            var unsetGlobals = missing.Where(UnsetSecrets.Contains).ToList();
            hint = unsetGlobals.Count > 0
                ? $"Secret values are kept on each computer and not shared through git: enter {string.Join(", ", unsetGlobals)} in the workspace's Globals."
                : "No environment is selected.";
        }
        else
        {
            var unsetSecrets = missing.Where(UnsetSecrets.Contains).ToList();
            hint = unsetSecrets.Count > 0
                ? $"Secret values are kept on each computer and not shared through git: enter {string.Join(", ", unsetSecrets)} in the environment '{EnvironmentName}'."
                : $"Add them to the environment '{EnvironmentName}'.";
        }

        return builtInHint is null ? hint : $"{hint} {builtInHint}";
    }

    /// <summary>Errors with the hint on undefined variables, as one message.</summary>
    public string Explain(IReadOnlyList<string> errors, IReadOnlyList<string> missing) =>
        string.Join(" ", MissingVariablesHint(missing) is { } hint ? [.. errors, hint] : errors);
}

/// <summary>Reads the variables of the selected environment of the open workspace.</summary>
public sealed class ActiveEnvironment(
    IEnvironmentStore store,
    ISecretStore secrets,
    ICapturedValueStore captures,
    IWorkspaceStateStore state,
    CurrentWorkspace current,
    IWorkspaceVariablesStore? globals = null)
{
    /// <summary>
    /// The selected environment's variables, with the values requests captured for it taking
    /// precedence over the file's values, and the workspace-global variables it doesn't define.
    /// Without a selected environment (or when the selected file no longer exists) only the globals
    /// apply; without a workspace there are none. An environment that can't be read
    /// or whose variables refer to each other in a loop is an error.
    /// </summary>
    public async Task<Result<ActiveVariables>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (current.Root is not { } root)
        {
            return ActiveVariables.None;
        }

        var id = (await state.LoadAsync(root, cancellationToken)).ActiveEnvironment;
        if (id is null || !EnvironmentMapper.IsValidId(id) || !store.Exists(root, id))
        {
            return await ResolveAsync(root, null, null, cancellationToken);
        }

        var environment = await store.ReadAsync(root, id, cancellationToken);
        if (!environment.IsSuccess)
        {
            return environment.Error!.Code == ErrorCodes.NotFound
                ? await ResolveAsync(root, null, null, cancellationToken)
                : new Error(environment.Error.Code, $"The selected environment can't be used. {environment.Error.Message}");
        }

        return await ResolveAsync(root, id, environment.Value, cancellationToken);
    }

    /// <summary>
    /// The variables of the environment file <paramref name="id"/> (such as <c>dev.yaml</c>),
    /// whichever environment is selected, resolved like <see cref="LoadAsync(CancellationToken)"/>.
    /// Unlike the selected one, an environment asked for by name must exist.
    /// </summary>
    public async Task<Result<ActiveVariables>> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!EnvironmentMapper.IsValidId(id))
        {
            return EnvironmentMapper.InvalidId(id);
        }

        if (!store.Exists(root.Value, id))
        {
            return EnvironmentMapper.NotFound(id);
        }

        var environment = await store.ReadAsync(root.Value, id, cancellationToken);
        if (!environment.IsSuccess)
        {
            return environment.Error!.Code == ErrorCodes.NotFound
                ? EnvironmentMapper.NotFound(id)
                : new Error(environment.Error.Code, $"The environment '{id}' can't be used. {environment.Error.Message}");
        }

        return await ResolveAsync(root.Value, id, environment.Value, cancellationToken);
    }

    private async Task<Result<ActiveVariables>> ResolveAsync(string root, string? id, EnvironmentDefinition? environment, CancellationToken cancellationToken)
    {
        var stored = await secrets.LoadAsync(root, cancellationToken);
        if (!stored.IsSuccess)
        {
            return stored.Error!;
        }

        var secretValues = id is null ? null : stored.Value.GetValueOrDefault(id);
        var unset = new SortedSet<string>(StringComparer.Ordinal);
        var variables = new List<EnvironmentVariable>();
        foreach (var variable in environment?.Variables ?? [])
        {
            if (!variable.Secret)
            {
                variables.Add(variable);
            }
            else if (secretValues?.GetValueOrDefault(variable.Name) is { Length: > 0 } value)
            {
                variables.Add(variable with { Value = value });
            }
            else
            {
                unset.Add(variable.Name);
            }
        }

        IReadOnlyDictionary<string, string> capturedValues = new Dictionary<string, string>();
        if (id is not null)
        {
            var captured = await captures.LoadAsync(root, cancellationToken);
            if (!captured.IsSuccess)
            {
                return captured.Error!;
            }

            capturedValues = captured.Value.GetValueOrDefault(id) ?? new Dictionary<string, string>();
        }

        // Captured values come from responses: they are used as they are, never expanded as
        // templates, so a value such as "{{token}}" can't refer to other variables or loop. They
        // take part in resolution as placeholders that no file value can contain.
        var placeholders = capturedValues.Keys
            .Order(StringComparer.Ordinal)
            .Select((name, index) => (Name: name, Placeholder: $"\0captured:{index}\0"))
            .ToList();
        if (placeholders.Count > 0)
        {
            variables.RemoveAll(variable => capturedValues.ContainsKey(variable.Name));
            unset.ExceptWith(capturedValues.Keys);
            variables.AddRange(placeholders.Select(p => new EnvironmentVariable(p.Name, p.Placeholder)));
        }

        // Workspace-global variables come last: the environment's (and captured) values win.
        var globalNames = new HashSet<string>(StringComparer.Ordinal);
        var globalSecrets = new List<string>();
        if (globals is not null)
        {
            var loaded = await GlobalVariables.LoadWithSecretsAsync(globals, secrets, root, cancellationToken);
            if (!loaded.IsSuccess)
            {
                return loaded.Error!;
            }

            var defined = new HashSet<string>(variables.Select(v => v.Name).Concat(unset), StringComparer.Ordinal);
            foreach (var variable in loaded.Value.Variables.Where(v => !defined.Contains(v.Name)))
            {
                variables.Add(variable);
                globalNames.Add(variable.Name);
                if (variable.Secret)
                {
                    globalSecrets.Add(variable.Name);
                }
            }

            unset.UnionWith(loaded.Value.UnsetSecrets.Where(name => !defined.Contains(name)));
        }

        var resolved = VariableSet.Resolve(variables);
        if (resolved.Cycle.Count > 0)
        {
            var owner = environment is null
                ? $"the workspace's {GlobalVariables.DisplayName.ToLowerInvariant()}"
                : resolved.Cycle.Any(globalNames.Contains)
                    ? $"the environment '{environment.Name}' and the workspace's globals"
                    : $"the environment '{environment.Name}'";
            return new Error(ErrorCodes.InvalidRequest, $"{VariableSet.CycleMessage(resolved.Cycle)} Fix {owner}.");
        }

        if (placeholders.Count > 0)
        {
            var values = resolved.Values.ToDictionary(
                pair => pair.Key,
                pair => placeholders.Aggregate(pair.Value, (text, p) => text.Replace(p.Placeholder, capturedValues[p.Name], StringComparison.Ordinal)),
                StringComparer.Ordinal);
            resolved = resolved with { Values = values };
        }

        var resolvedSecrets = (environment?.Variables ?? [])
            .Where(v => v.Secret)
            .Select(v => v.Name)
            .Concat(globalSecrets)
            .Where(resolved.Values.ContainsKey)
            .Select(name => resolved.Values[name])
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new ActiveVariables(environment?.Name, resolved.Values, unset, id, resolvedSecrets, environment?.CookieJar ?? false)
        {
            Globals = globalNames,
        };
    }
}
