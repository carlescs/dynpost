using Missive.Domain.Git;
using Missive.Infrastructure.Git;

namespace Missive.Infrastructure.Tests.Git;

public class PorcelainStatusParserTests
{
    private const string Hash = "d00491fd7e5bb6fa28c517a0bb32b8b506539d4d";

    private static string Output(params string[] records) => string.Join('\0', records) + '\0';

    [Fact]
    public void ParsesBranchHeaders()
    {
        var status = PorcelainStatusParser.Parse(
            Output(
                $"# branch.oid {Hash}",
                "# branch.head feature/login",
                "# branch.upstream origin/feature/login",
                "# branch.ab +2 -3"),
            "",
            isMerging: false);

        Assert.Equal("feature/login", status.Branch);
        Assert.Equal("origin/feature/login", status.Upstream);
        Assert.Equal(2, status.Ahead);
        Assert.Equal(3, status.Behind);
        Assert.Empty(status.Changes);
    }

    [Fact]
    public void DetachedHead_HasNoBranch()
    {
        var status = PorcelainStatusParser.Parse(Output($"# branch.oid {Hash}", "# branch.head (detached)"), "", true);

        Assert.Null(status.Branch);
        Assert.Null(status.Upstream);
        Assert.True(status.IsMerging);
    }

    [Fact]
    public void ParsesEntryTypes()
    {
        var status = PorcelainStatusParser.Parse(
            Output(
                "# branch.head main",
                $"1 .M N... 100644 100644 100644 {Hash} {Hash} modified.yaml",
                $"1 A. N... 000000 100644 100644 {Hash} {Hash} added.yaml",
                $"1 AM N... 000000 100644 100644 {Hash} {Hash} added-then-edited.yaml",
                $"1 .D N... 100644 100644 000000 {Hash} {Hash} deleted.yaml",
                $"1 D. N... 100644 000000 000000 {Hash} {Hash} removed.yaml",
                $"2 R. N... 100644 100644 100644 {Hash} {Hash} R100 renamed.yaml",
                "original.yaml",
                $"2 C. N... 100644 100644 100644 {Hash} {Hash} C75 copy.yaml",
                "source.yaml",
                $"u UU N... 100644 100644 100644 100644 {Hash} {Hash} {Hash} conflicted.yaml",
                "? with space.yaml"),
            "",
            false);

        Assert.Equal(
            new GitChange[]
            {
                new GitChange("added-then-edited.yaml", GitFileStatus.Added),
                new GitChange("added.yaml", GitFileStatus.Added),
                new GitChange("conflicted.yaml", GitFileStatus.Conflicted),
                new GitChange("copy.yaml", GitFileStatus.Added),
                new GitChange("deleted.yaml", GitFileStatus.Deleted),
                new GitChange("modified.yaml", GitFileStatus.Modified),
                new GitChange("removed.yaml", GitFileStatus.Deleted),
                new GitChange("renamed.yaml", GitFileStatus.Renamed, "original.yaml"),
                new GitChange("with space.yaml", GitFileStatus.Untracked),
            },
            status.Changes);
    }

    [Fact]
    public void MakesPathsRelativeToTheWorkspace_AndDropsOthers()
    {
        var status = PorcelainStatusParser.Parse(
            Output(
                $"1 .M N... 100644 100644 100644 {Hash} {Hash} api/users/get user.yaml",
                $"1 .M N... 100644 100644 100644 {Hash} {Hash} api-other/file.yaml",
                $"1 .M N... 100644 100644 100644 {Hash} {Hash} README.md",
                $"2 R. N... 100644 100644 100644 {Hash} {Hash} R100 api/moved-in.yaml",
                "elsewhere/moved-in.yaml",
                $"2 R. N... 100644 100644 100644 {Hash} {Hash} R100 elsewhere/moved-out.yaml",
                "api/moved-out.yaml",
                $"2 R. N... 100644 100644 100644 {Hash} {Hash} R100 api/new-name.yaml",
                "api/old-name.yaml",
                "? api/new.yaml"),
            "api/",
            false);

        Assert.Equal(
            new GitChange[]
            {
                new GitChange("moved-in.yaml", GitFileStatus.Added),
                new GitChange("moved-out.yaml", GitFileStatus.Deleted),
                new GitChange("new-name.yaml", GitFileStatus.Renamed, "old-name.yaml"),
                new GitChange("new.yaml", GitFileStatus.Untracked),
                new GitChange("users/get user.yaml", GitFileStatus.Modified),
            },
            status.Changes);
    }
}
