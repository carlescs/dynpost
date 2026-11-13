using Dynypost.Application.Common;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Git;

namespace Dynypost.Application.Git;

/// <summary>Lists the remotes of the open workspace's repository.</summary>
public sealed class ListRemotes(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<IReadOnlyList<GitRemote>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        return await git.ListRemotesAsync(root.Value, cancellationToken);
    }
}

public sealed record AddRemoteRequest(string Name, string Url);

/// <summary>Adds a remote to the open workspace's repository. Nothing is fetched or pushed.</summary>
public sealed class AddRemote(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<IReadOnlyList<GitRemote>>> ExecuteAsync(AddRemoteRequest request, CancellationToken cancellationToken = default)
    {
        if (!RemoteName.TryCreate(request.Name, out var name, out var nameError))
        {
            return GitErrors.InvalidRemote(nameError);
        }

        if (!RemoteUrl.TryCreate(request.Url, out var url, out var urlError))
        {
            return GitErrors.InvalidRemote(urlError);
        }

        var context = await RemoteContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, remotes) = context.Value;
        if (RemoteContext.Find(remotes, name.Value) is not null)
        {
            return GitErrors.RemoteExists(name.Value);
        }

        var added = await git.AddRemoteAsync(root, name, url, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error!;
        }

        return await git.ListRemotesAsync(root, cancellationToken);
    }
}

/// <param name="Name">The remote to change.</param>
/// <param name="NewName">Its new name; the same name keeps it.</param>
/// <param name="Url">Its new URL; the current fetch URL keeps it.</param>
public sealed record UpdateRemoteRequest(string Name, string NewName, string Url);

/// <summary>Renames a remote and/or changes its URL.</summary>
public sealed class UpdateRemote(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<IReadOnlyList<GitRemote>>> ExecuteAsync(UpdateRemoteRequest request, CancellationToken cancellationToken = default)
    {
        if (!RemoteUrl.TryCreate(request.Url, out var url, out var urlError))
        {
            return GitErrors.InvalidRemote(urlError);
        }

        var context = await RemoteContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, remotes) = context.Value;
        if (RemoteContext.Find(remotes, request.Name) is not { } remote)
        {
            return GitErrors.RemoteNotFound(request.Name);
        }

        var currentName = remote.Name;
        if (!string.Equals(request.NewName?.Trim(), remote.Name, StringComparison.Ordinal))
        {
            if (!RemoteName.TryCreate(request.NewName, out var newName, out var nameError))
            {
                return GitErrors.InvalidRemote(nameError);
            }

            if (RemoteContext.Find(remotes, newName.Value) is not null)
            {
                return GitErrors.RemoteExists(newName.Value);
            }

            var renamed = await git.RenameRemoteAsync(root, remote.Name, newName, cancellationToken);
            if (renamed.IsFailure)
            {
                return renamed.Error!;
            }

            currentName = newName.Value;
        }

        if (!string.Equals(url.Value, remote.FetchUrl, StringComparison.Ordinal))
        {
            var set = await git.SetRemoteUrlAsync(root, currentName, url, cancellationToken);
            if (set.IsFailure)
            {
                return set.Error!;
            }
        }

        return await git.ListRemotesAsync(root, cancellationToken);
    }
}

/// <summary>Removes a remote, at the user's request.</summary>
public sealed class RemoveRemote(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<IReadOnlyList<GitRemote>>> ExecuteAsync(string name, CancellationToken cancellationToken = default)
    {
        var context = await RemoteContext.LoadAsync(git, workspace, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var (root, remotes) = context.Value;
        if (RemoteContext.Find(remotes, name) is not { } remote)
        {
            return GitErrors.RemoteNotFound(name);
        }

        var removed = await git.RemoveRemoteAsync(root, remote.Name, cancellationToken);
        if (removed.IsFailure)
        {
            return removed.Error!;
        }

        return await git.ListRemotesAsync(root, cancellationToken);
    }
}

/// <summary>The open workspace and its current remotes.</summary>
internal static class RemoteContext
{
    public static async Task<Result<(string Root, IReadOnlyList<GitRemote> Remotes)>> LoadAsync(
        IGitService git,
        CurrentWorkspace workspace,
        CancellationToken cancellationToken)
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

        return Result<(string Root, IReadOnlyList<GitRemote> Remotes)>.Success((root.Value, remotes.Value));
    }

    /// <summary>Only existing remotes can be changed, so a name can't be read as anything else.</summary>
    public static GitRemote? Find(IReadOnlyList<GitRemote> remotes, string? name) =>
        remotes.FirstOrDefault(r => string.Equals(r.Name, name?.Trim(), StringComparison.Ordinal));
}
