namespace Dynpost.Domain.Git;

/// <summary>
/// One of a repository's working trees: the main one, or a linked one (<c>git worktree add</c>)
/// with another branch checked out in its own folder.
/// </summary>
/// <param name="Path">The worktree's folder, the top of its checkout, as git reports it.</param>
/// <param name="WorkspaceFolder">
/// The workspace folder in it: <see cref="Path"/>, or its subfolder at the place the open workspace
/// has in its own worktree.
/// </param>
/// <param name="Branch">The checked-out branch; <c>null</c> when HEAD is detached.</param>
/// <param name="Head">The checked-out commit; empty before the first commit.</param>
/// <param name="IsMain">Whether it is the main worktree, where the repository's <c>.git</c> folder is.</param>
/// <param name="IsCurrent">Whether it is the worktree of the open workspace.</param>
/// <param name="IsLocked">Whether it is locked against removal (<c>git worktree lock</c>).</param>
/// <param name="IsPrunable">Whether its folder is gone, so git only remembers it until it is pruned.</param>
public sealed record GitWorktree(
    string Path,
    string WorkspaceFolder,
    string? Branch,
    string Head,
    bool IsMain,
    bool IsCurrent,
    bool IsLocked = false,
    bool IsPrunable = false)
{
    public bool IsDetached => Branch is null;
}
