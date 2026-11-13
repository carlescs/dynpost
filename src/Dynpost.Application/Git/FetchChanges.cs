using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;

namespace Dynpost.Application.Git;

/// <summary>
/// Downloads the remotes' latest commits without changing any file, so the user can see what a
/// pull would bring in.
/// </summary>
public sealed class FetchChanges(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitStatus>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var remotes = await git.ListRemotesAsync(root.Value, cancellationToken);
        if (remotes.IsFailure)
        {
            return remotes.Error!;
        }

        if (remotes.Value.Count == 0)
        {
            return GitErrors.NoRemote();
        }

        var fetch = await git.FetchAsync(root.Value, cancellationToken);
        if (fetch.IsFailure)
        {
            return fetch.Error!;
        }

        return await git.GetStatusAsync(root.Value, cancellationToken);
    }
}
