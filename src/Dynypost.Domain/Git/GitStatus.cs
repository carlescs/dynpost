namespace Dynypost.Domain.Git;

/// <summary>A snapshot of the workspace's git state.</summary>
/// <param name="Branch">The checked-out branch, or <c>null</c> when HEAD is detached.</param>
/// <param name="Upstream">The branch's upstream (for example <c>origin/main</c>), or <c>null</c> when none is set.</param>
/// <param name="Ahead">Local commits not yet pushed to the upstream.</param>
/// <param name="Behind">Upstream commits not yet pulled, as of the last fetch.</param>
/// <param name="IsMerging">Whether a merge is in progress (for example after a pull that conflicted).</param>
/// <param name="Changes">Changed files inside the workspace folder.</param>
/// <param name="MergeMessage">During a merge, git's proposed commit message (without comment lines).</param>
/// <param name="IsUnborn">Whether the branch has no commits yet (a new repository).</param>
/// <param name="IsUpstreamGone">Whether the upstream is set but its branch no longer exists on the remote, as of the last fetch.</param>
public sealed record GitStatus(
    string? Branch,
    string? Upstream,
    int Ahead,
    int Behind,
    bool IsMerging,
    IReadOnlyList<GitChange> Changes,
    string? MergeMessage = null,
    bool IsUnborn = false,
    bool IsUpstreamGone = false)
{
    public bool IsDetached => Branch is null;

    public bool HasUpstream => Upstream is not null;

    public bool HasChanges => Changes.Count > 0;

    public bool HasConflicts => Changes.Any(c => c.Status == GitFileStatus.Conflicted);

    public IReadOnlyList<GitChange> Conflicts => Changes.Where(c => c.Status == GitFileStatus.Conflicted).ToList();
}
