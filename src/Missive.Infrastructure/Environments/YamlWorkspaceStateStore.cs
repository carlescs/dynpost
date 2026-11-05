using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Infrastructure.Yaml;
using static Missive.Infrastructure.Workspaces.WorkspaceFiles;
using static Missive.Infrastructure.Yaml.YamlNodes;

namespace Missive.Infrastructure.Environments;

/// <summary>Keeps <see cref="WorkspaceState"/> in <c>.missive/state.yaml</c>, which is git-ignored.</summary>
public sealed class YamlWorkspaceStateStore : IWorkspaceStateStore
{
    public const string FileName = "state.yaml";

    public async Task<WorkspaceState> LoadAsync(string root, CancellationToken cancellationToken = default)
    {
        try
        {
            var text = await ReadIfExistsAsync(LocalWorkspaceFolder.FilePath(root, FileName), cancellationToken);
            return text is null
                ? WorkspaceState.Default
                : new WorkspaceState(OptionalString(LoadMapping(text), "environment") is { Length: > 0 } id ? id : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorkspaceFileFormatException)
        {
            // Lost UI state is not worth failing over; start from the defaults.
            return WorkspaceState.Default;
        }
    }

    public async Task<Result<Unit>> SaveAsync(string root, WorkspaceState state, CancellationToken cancellationToken = default)
    {
        try
        {
            if (state == WorkspaceState.Default)
            {
                LocalWorkspaceFolder.Delete(root, FileName);
                return Unit.Value;
            }

            var writer = new YamlWriter().Scalar("environment", state.ActiveEnvironment!);
            await LocalWorkspaceFolder.WriteAsync(root, FileName, writer.ToString(), cancellationToken);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not save the workspace state: {ex.Message}");
        }
    }
}
