using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;

namespace Dynpost.Application.Git;

/// <summary>Reads the name and email git records on the user's commits in the open workspace.</summary>
public sealed class GetGitIdentity(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitIdentitySettings>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        return await git.GetIdentityAsync(root.Value, cancellationToken);
    }
}

/// <param name="Global">Use the identity in all of the user's repositories, not only this workspace's.</param>
public sealed record SetGitIdentityRequest(string Name, string Email, bool Global = true);

/// <summary>Sets the name and email git records on commits, so the user can commit without a terminal.</summary>
public sealed class SetGitIdentity(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<GitIdentitySettings>> ExecuteAsync(
        SetGitIdentityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!GitIdentity.TryCreate(request.Name, request.Email, out var identity))
        {
            return GitErrors.InvalidIdentity();
        }

        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var set = await git.SetIdentityAsync(root.Value, identity, request.Global, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error!;
        }

        return await git.GetIdentityAsync(root.Value, cancellationToken);
    }
}
