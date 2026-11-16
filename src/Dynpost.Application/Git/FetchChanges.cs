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
    /// <param name="signIn">Credentials the user entered after a first try couldn't sign in to the remote.</param>
    public async Task<Result<GitStatus>> ExecuteAsync(GitSignIn? signIn = null, CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var credentials = GitSignIn.Check(signIn);
        if (credentials.IsFailure)
        {
            return credentials.Error!;
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

        var fetch = await git.FetchAsync(root.Value, credentials.Value, cancellationToken);
        if (fetch.IsFailure)
        {
            return fetch.Error!;
        }

        return await git.GetStatusAsync(root.Value, cancellationToken);
    }
}
