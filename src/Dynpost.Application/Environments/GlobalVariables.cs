using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Environments;

namespace Dynpost.Application.Environments;

/// <summary>
/// The workspace-global variables, in the workspace's <c>dynpost.yaml</c>: <c>variables</c> and the
/// names of <c>secrets</c>, written like an environment's. Secret values stay in
/// <see cref="ISecretStore"/>, under <see cref="GlobalVariables.SecretsKey"/>.
/// </summary>
public interface IWorkspaceVariablesStore
{
    /// <summary>The global variables, secrets with empty values; none when the file or keys are missing.</summary>
    Task<Result<IReadOnlyList<EnvironmentVariable>>> ReadAsync(string root, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the global variables into <c>dynpost.yaml</c>, keeping its other keys (such as
    /// <c>layout</c>). Leaves the file untouched when its content would not change; the result says
    /// whether it was written.
    /// </summary>
    Task<Result<bool>> WriteAsync(string root, IReadOnlyList<EnvironmentVariable> variables, CancellationToken cancellationToken = default);
}

/// <summary>The workspace-global variables, as the UI edits them (secrets with this computer's values).</summary>
public sealed record GlobalVariablesDto(IReadOnlyList<EnvironmentVariableDto> Variables);

public static class GlobalVariables
{
    /// <summary>
    /// The key of the global secrets in <see cref="ISecretStore"/>: never an environment's file
    /// name, since file names can't contain a colon on every system.
    /// </summary>
    public const string SecretsKey = ":globals";

    /// <summary>The name globals are shown and reported under.</summary>
    public const string DisplayName = "Globals";

    /// <summary>The global variables with this computer's secret values; unset secrets are listed apart.</summary>
    internal static async Task<Result<(List<EnvironmentVariable> Variables, SortedSet<string> UnsetSecrets)>> LoadWithSecretsAsync(
        IWorkspaceVariablesStore store,
        ISecretStore secrets,
        string root,
        CancellationToken cancellationToken)
    {
        var globals = await store.ReadAsync(root, cancellationToken);
        if (!globals.IsSuccess)
        {
            return globals.Error!;
        }

        var unset = new SortedSet<string>(StringComparer.Ordinal);
        var variables = new List<EnvironmentVariable>();
        if (globals.Value.Count == 0)
        {
            return (variables, unset);
        }

        var stored = await secrets.LoadAsync(root, cancellationToken);
        if (!stored.IsSuccess)
        {
            return stored.Error!;
        }

        var values = stored.Value.GetValueOrDefault(SecretsKey);
        foreach (var variable in globals.Value)
        {
            if (!variable.Secret)
            {
                variables.Add(variable);
            }
            else if (values?.GetValueOrDefault(variable.Name) is { Length: > 0 } value)
            {
                variables.Add(variable with { Value = value });
            }
            else
            {
                unset.Add(variable.Name);
            }
        }

        return (variables, unset);
    }
}

/// <summary>Reads the workspace-global variables, with the values of their secrets from this computer.</summary>
public sealed class LoadGlobalVariables(IWorkspaceVariablesStore store, ISecretStore secrets, CurrentWorkspace current)
{
    public async Task<Result<GlobalVariablesDto>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var globals = await store.ReadAsync(root.Value, cancellationToken);
        if (!globals.IsSuccess)
        {
            return globals.Error!;
        }

        var stored = await secrets.LoadAsync(root.Value, cancellationToken);
        if (!stored.IsSuccess)
        {
            return stored.Error!;
        }

        var dto = EnvironmentMapper.ToDto(
            new EnvironmentDefinition(GlobalVariables.DisplayName, globals.Value),
            stored.Value.GetValueOrDefault(GlobalVariables.SecretsKey));
        return new GlobalVariablesDto(dto.Variables);
    }
}

/// <summary>
/// Saves the workspace-global variables in <c>dynpost.yaml</c> and their secret values on this
/// computer. Returns them as saved.
/// </summary>
public sealed class SaveGlobalVariables(IWorkspaceVariablesStore store, ISecretStore secrets, CurrentWorkspace current)
{
    public async Task<Result<GlobalVariablesDto>> ExecuteAsync(GlobalVariablesDto dto, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var globals = EnvironmentMapper.ToDomain(new EnvironmentDto(GlobalVariables.DisplayName, dto.Variables ?? []));
        if (!globals.IsSuccess)
        {
            return globals.Error!;
        }

        var stored = await secrets.LoadAsync(root.Value, cancellationToken);
        if (!stored.IsSuccess)
        {
            return stored.Error!;
        }

        var written = await store.WriteAsync(root.Value, globals.Value.Variables, cancellationToken);
        if (!written.IsSuccess)
        {
            return written.Error!;
        }

        var saved = await EnvironmentFiles.SaveSecretsAsync(
            secrets,
            root.Value,
            stored.Value,
            GlobalVariables.SecretsKey,
            null,
            globals.Value,
            cancellationToken);
        return saved.IsSuccess
            ? new GlobalVariablesDto(EnvironmentMapper.ToDto(globals.Value, EnvironmentMapper.SecretValues(globals.Value)).Variables)
            : saved.Error!;
    }
}
