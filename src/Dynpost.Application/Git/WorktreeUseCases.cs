using Dynpost.Application.Certificates;
using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Secrets;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;
using Dynpost.Domain.Http;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Git;

/// <summary>Lists the worktrees of the open workspace's repository, the main one first.</summary>
public sealed class ListWorktrees(IGitService git, CurrentWorkspace workspace)
{
    public async Task<Result<IReadOnlyList<GitWorktree>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        return root.IsFailure ? root.Error! : await git.ListWorktreesAsync(root.Value, cancellationToken);
    }
}

/// <param name="ParentFolder">The existing folder the worktree's folder is created in.</param>
/// <param name="Name">The new folder's name.</param>
/// <param name="Branch">The local branch to check out, or the new branch's name.</param>
/// <param name="CreateBranch">Whether to create <see cref="Branch"/> at the current commit.</param>
public sealed record CreateWorktreeRequest(string ParentFolder, string Name, string Branch, bool CreateBranch = false);

/// <param name="Worktree">The new worktree.</param>
/// <param name="Worktrees">Every worktree afterwards.</param>
/// <param name="Warning">
/// What went wrong after the worktree was created (it has no workspace folder, or local data could
/// not all be copied); <c>null</c> when nothing did.
/// </param>
public sealed record CreateWorktreeResult(GitWorktree Worktree, IReadOnlyList<GitWorktree> Worktrees, string? Warning);

/// <summary>
/// Checks out a local branch, or a new branch at the current commit, in a new folder linked to the
/// same repository (<c>git worktree add</c>), so two branches can be worked on side by side. The
/// workspace's local settings that git doesn't carry (secret values, the <c>.env</c> file and
/// client certificates) are copied to the new worktree's workspace folder, so its requests work
/// straight away; cookies and response history start empty.
/// </summary>
public sealed class CreateWorktree(
    IGitService git,
    CurrentWorkspace workspace,
    IWorkspaceRepository repository,
    ISecretStore secrets,
    IClientCertificateStore certificates,
    IDotEnvValues dotEnv)
{
    public async Task<Result<CreateWorktreeResult>> ExecuteAsync(CreateWorktreeRequest request, CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        if (string.IsNullOrWhiteSpace(request.ParentFolder) || !repository.FolderExists(request.ParentFolder))
        {
            return new Error(ErrorCodes.NotFound, $"The folder '{request.ParentFolder}' does not exist.");
        }

        var name = request.Name?.Trim();
        if (CloneRepository.CheckFolderName(name) is { } nameError)
        {
            return GitErrors.InvalidWorktreeFolder(nameError);
        }

        var current = await BranchContext.ReadAsync(git, root.Value, cancellationToken);
        if (current.IsFailure)
        {
            return current.Error!;
        }

        if (current.Value.Status.IsUnborn)
        {
            return GitErrors.NoCommitsForWorktree();
        }

        var branches = current.Value.Branches;
        string branch;
        if (request.CreateBranch)
        {
            if (!BranchName.TryCreate(request.Branch, out var newName, out var branchError))
            {
                return GitErrors.InvalidBranchName(branchError);
            }

            if (branches.Any(b => !b.IsRemote && string.Equals(b.Name, newName.Value, StringComparison.OrdinalIgnoreCase)))
            {
                return GitErrors.BranchExists(newName.Value);
            }

            branch = newName.Value;
        }
        else if (BranchContext.Local(branches, request.Branch) is { } existing)
        {
            branch = existing.Name;
        }
        else
        {
            return GitErrors.BranchNotFound(request.Branch?.Trim() ?? string.Empty);
        }

        var folder = Path.Combine(request.ParentFolder, name!);
        var added = await git.AddWorktreeAsync(root.Value, folder, branch, request.CreateBranch, cancellationToken);
        if (added.IsFailure)
        {
            return added.Error!;
        }

        var worktrees = await git.ListWorktreesAsync(root.Value, cancellationToken);
        if (worktrees.IsFailure)
        {
            return worktrees.Error!;
        }

        // A branch is checked out in one worktree at most, so it names the new one.
        if (worktrees.Value.FirstOrDefault(w => w.Branch == branch) is not { } created)
        {
            return GitErrors.CommandFailed("worktree add", $"The new worktree for '{branch}' is not in git's list of worktrees.");
        }

        var warning = repository.FolderExists(created.WorkspaceFolder)
            ? await CopyLocalDataAsync(root.Value, created.WorkspaceFolder, cancellationToken)
            : $"'{branch}' has no workspace folder at '{created.WorkspaceFolder}', so there is nothing to open in the new worktree.";
        return new CreateWorktreeResult(created, worktrees.Value, warning);
    }

    /// <summary>Copies what requests need that git doesn't carry; returns what could not be copied, if anything.</summary>
    private async Task<string?> CopyLocalDataAsync(string from, string to, CancellationToken cancellationToken)
    {
        var failures = new List<string>();

        var values = await secrets.LoadAsync(from, cancellationToken);
        if (values.IsFailure)
        {
            failures.Add($"secret values ({values.Error!.Message})");
        }
        else if (values.Value.Count > 0 && await secrets.SaveAsync(to, values.Value, cancellationToken) is { IsFailure: true } savedSecrets)
        {
            failures.Add($"secret values ({savedSecrets.Error!.Message})");
        }

        var settings = await certificates.LoadAsync(from, cancellationToken);
        if (settings.IsFailure)
        {
            failures.Add($"client certificates ({settings.Error!.Message})");
        }
        else if (settings.Value.Count > 0)
        {
            var moved = settings.Value.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<ClientCertificateSetting>)entry.Value
                    .Select(c => c with { Certificate = Locate(c.Certificate, from, to), Key = Locate(c.Key, from, to) })
                    .ToList(),
                StringComparer.Ordinal);
            if (await certificates.SaveAsync(to, moved, cancellationToken) is { IsFailure: true } savedCertificates)
            {
                failures.Add($"client certificates ({savedCertificates.Error!.Message})");
            }
        }

        if (await dotEnv.CopyAsync(from, to, cancellationToken) is { IsFailure: true } copied)
        {
            failures.Add($"the .env file ({copied.Error!.Message})");
        }

        return failures.Count == 0
            ? null
            : $"The worktree was created, but Dynpost couldn't copy {string.Join(", ", failures)}. Set them up in the worktree again.";
    }

    /// <summary>
    /// A certificate file path for the new worktree: a workspace-relative file that git doesn't
    /// carry (it isn't in the new worktree) is named by its full path in the original workspace.
    /// </summary>
    private string Locate(string path, string from, string to)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return path;
        }

        return WorkspacePath.TryParse(path, out var relative) && repository.FileExists(to, relative)
            ? path
            : Path.GetFullPath(Path.Combine(from, path));
    }
}

/// <param name="Path">The worktree's folder, as listed.</param>
/// <param name="Force">
/// Remove it even with uncommitted changes or new files: only after the user confirmed a second
/// time that they will be lost.
/// </param>
public sealed record RemoveWorktreeRequest(string Path, bool Force = false);

/// <summary>
/// Removes a linked worktree and its folder; its branch and commits stay in the repository. The
/// main worktree and the open one can't be removed, nor a locked one. Secret values copied to it
/// are deleted too, so none are left behind in the system's credential store. A worktree whose
/// folder is already gone is just forgotten (<c>git worktree prune</c>).
/// </summary>
public sealed class RemoveWorktree(IGitService git, CurrentWorkspace workspace, IWorkspaceRepository repository, ISecretStore secrets)
{
    public async Task<Result<IReadOnlyList<GitWorktree>>> ExecuteAsync(RemoveWorktreeRequest request, CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var worktrees = await git.ListWorktreesAsync(root.Value, cancellationToken);
        if (worktrees.IsFailure)
        {
            return worktrees.Error!;
        }

        // Only listed worktrees are acted on, so a path can't point anywhere else.
        if (worktrees.Value.FirstOrDefault(w => w.Path == request.Path) is not { } worktree)
        {
            return GitErrors.WorktreeNotFound(request.Path ?? string.Empty);
        }

        if (worktree.IsMain)
        {
            return GitErrors.MainWorktree();
        }

        if (worktree.IsCurrent)
        {
            return GitErrors.CurrentWorktree();
        }

        if (worktree.IsLocked)
        {
            return GitErrors.WorktreeLocked(worktree.Path);
        }

        var removed = worktree.IsPrunable
            ? await git.PruneWorktreesAsync(root.Value, cancellationToken)
            : await RemoveWithSecretsAsync(root.Value, worktree, request.Force, cancellationToken);
        return removed.IsFailure ? removed.Error! : await git.ListWorktreesAsync(root.Value, cancellationToken);
    }

    /// <summary>
    /// Deletes the worktree's secrets while its folder still lists them, then removes it; if git
    /// keeps the worktree after all, its secrets are put back.
    /// </summary>
    private async Task<Result<Unit>> RemoveWithSecretsAsync(string root, GitWorktree worktree, bool force, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> kept = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        if (repository.FolderExists(worktree.WorkspaceFolder))
        {
            var values = await secrets.LoadAsync(worktree.WorkspaceFolder, cancellationToken);
            if (values.IsFailure)
            {
                return values.Error!;
            }

            kept = values.Value;
            if (kept.Count > 0
                && await secrets.SaveAsync(worktree.WorkspaceFolder, new Dictionary<string, IReadOnlyDictionary<string, string>>(), cancellationToken)
                    is { IsFailure: true } cleared)
            {
                return cleared.Error!;
            }
        }

        var removed = await git.RemoveWorktreeAsync(root, worktree, force, cancellationToken);
        if (removed.IsFailure && kept.Count > 0)
        {
            await secrets.SaveAsync(worktree.WorkspaceFolder, kept, cancellationToken);
        }

        return removed;
    }
}
