using Dynypost.Domain.Git;

namespace Dynypost.Domain.Tests.Git;

public class GitStatusTests
{
    [Fact]
    public void Conflicts_ListsOnlyConflictedFiles()
    {
        var status = new GitStatus(
            "main",
            "origin/main",
            0,
            0,
            IsMerging: true,
            [new GitChange("a.yaml", GitFileStatus.Modified), new GitChange("b.yaml", GitFileStatus.Conflicted)]);

        Assert.True(status.HasChanges);
        Assert.True(status.HasConflicts);
        Assert.Equal(new[] { "b.yaml" }, status.Conflicts.Select(c => c.Path));
    }

    [Fact]
    public void CleanDetachedStatus_HasNoChangesBranchOrUpstream()
    {
        var status = new GitStatus(null, null, 0, 0, false, []);

        Assert.True(status.IsDetached);
        Assert.False(status.HasUpstream);
        Assert.False(status.HasChanges);
        Assert.False(status.HasConflicts);
    }
}
