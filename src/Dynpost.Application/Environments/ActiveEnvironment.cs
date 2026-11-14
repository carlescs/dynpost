using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Environments;

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

    /// <summary>Explains where the values of undefined variables should come from; <c>null</c> when none are missing.</summary>
    public string? MissingVariablesHint(IReadOnlyList<string> missing)
    {
        if (missing.Count == 0)
        {
            return null;
        }

        if (EnvironmentName is null)
        {
            return "No environment is selected.";
        }

        var unsetSecrets = missing.Where(UnsetSecrets.Contains).ToList();
        return unsetSecrets.Count > 0
            ? $"Secret values are kept on each computer and not shared through git: enter {string.Join(", ", unsetSecrets)} in the environment '{EnvironmentName}'."
            : $"Add them to the environment '{EnvironmentName}'.";
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
    CurrentWorkspace current)
{
    /// <summary>
    /// The selected environment's variables, with the values requests captured for it taking
    /// precedence over the file's values. Without a workspace or a selected environment (or
    /// when the selected file no longer exists) there are none. An environment that can't be read
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
            return ActiveVariables.None;
        }

        var environment = await store.ReadAsync(root, id, cancellationToken);
        if (!environment.IsSuccess)
        {
            return environment.Error!.Code == ErrorCodes.NotFound
                ? ActiveVariables.None
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

    private async Task<Result<ActiveVariables>> ResolveAsync(string root, string id, EnvironmentDefinition environment, CancellationToken cancellationToken)
    {

        var stored = await secrets.LoadAsync(root, cancellationToken);
        if (!stored.IsSuccess)
        {
            return stored.Error!;
        }

        var secretValues = stored.Value.GetValueOrDefault(id);
        var unset = new SortedSet<string>(StringComparer.Ordinal);
        var variables = new List<EnvironmentVariable>();
        foreach (var variable in environment.Variables)
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

        var captured = await captures.LoadAsync(root, cancellationToken);
        if (!captured.IsSuccess)
        {
            return captured.Error!;
        }

        // Captured values come from responses: they are used as they are, never expanded as
        // templates, so a value such as "{{token}}" can't refer to other variables or loop. They
        // take part in resolution as placeholders that no file value can contain.
        var capturedValues = captured.Value.GetValueOrDefault(id) ?? new Dictionary<string, string>();
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

        var resolved = VariableSet.Resolve(variables);
        if (resolved.Cycle.Count > 0)
        {
            return new Error(
                ErrorCodes.InvalidRequest,
                $"{VariableSet.CycleMessage(resolved.Cycle)} Fix the environment '{environment.Name}'.");
        }

        if (placeholders.Count > 0)
        {
            var values = resolved.Values.ToDictionary(
                pair => pair.Key,
                pair => placeholders.Aggregate(pair.Value, (text, p) => text.Replace(p.Placeholder, capturedValues[p.Name], StringComparison.Ordinal)),
                StringComparer.Ordinal);
            resolved = resolved with { Values = values };
        }

        var resolvedSecrets = environment.Variables
            .Where(v => v.Secret && resolved.Values.ContainsKey(v.Name))
            .Select(v => resolved.Values[v.Name])
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new ActiveVariables(environment.Name, resolved.Values, unset, id, resolvedSecrets, environment.CookieJar);
    }
}
