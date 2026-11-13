using Dynpost.Application.Common;
using Dynpost.Application.Git;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Sharing;

/// <summary>
/// Chooses whether a request, environment or run plan file is shared through git or kept on this
/// computer only. A file made local-only stops being tracked by git (the file itself stays on
/// disk), so the next commit removes it from the repository; sharing it again puts it back in the
/// list of changes to commit.
/// </summary>
public sealed class SetLocalOnly(
    ILocalOnlyFiles localFiles,
    IWorkspaceRepository repository,
    IGitService git,
    CurrentWorkspace current)
{
    public async Task<Result<SharingDto>> ExecuteAsync(string path, bool localOnly, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!WorkspacePath.TryParse(path, out var filePath) || !IsShareable(filePath))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{path}' is not a request, environment or run plan file.");
        }

        if (!repository.FileExists(root.Value, filePath))
        {
            return new Error(ErrorCodes.NotFound, $"The file '{path}' does not exist.");
        }

        var changed = localFiles.Set(root.Value, filePath, localOnly);
        if (!changed.IsSuccess)
        {
            return changed.Error!;
        }

        var tracking = localOnly
            ? await git.StopTrackingAsync(root.Value, filePath.Value, cancellationToken)
            : await git.RestoreTrackingAsync(root.Value, filePath.Value, cancellationToken);

        // Without git (or outside a repository) the file is simply ignored once there is one.
        if (tracking.IsFailure && !IsWithoutGit(tracking.Error!))
        {
            return tracking.Error!;
        }

        return new SharingDto(filePath.Value, localOnly);
    }

    /// <summary>Whether <paramref name="path"/> is a file whose sharing the user can choose.</summary>
    public static bool IsShareable(WorkspacePath path)
    {
        if (path.IsRoot)
        {
            return false;
        }

        if (WorkspaceLayout.IsRequestPath(path))
        {
            return true;
        }

        if (path.Parent.Value == WorkspaceLayout.EnvironmentsFolder)
        {
            return WorkspaceLayout.IsEnvironmentFile(path.FileName);
        }

        return WorkspaceLayout.IsRunPlanPath(path);
    }

    private static bool IsWithoutGit(Error error) =>
        error.Code is GitErrors.NotARepositoryCode or GitErrors.GitNotInstalledCode;
}
