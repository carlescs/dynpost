using Missive.Application.Common;
using Missive.Application.Git;
using Missive.Application.Workspaces;
using Missive.Domain.Git;
using NSubstitute;
using static Missive.Application.Tests.Git.GitTestData;

namespace Missive.Application.Tests.Git;

public class DiscardChangesTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly CurrentWorkspace _workspace = OpenedWorkspace();

    [Fact]
    public async Task WithoutPaths_IsRejected()
    {
        var result = await new DiscardChanges(_git, _workspace).ExecuteAsync([]);

        Assert.Equal(GitErrors.NothingToDiscardCode, result.Error?.Code);
    }

    [Fact]
    public async Task DuringAMerge_IsRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(merging: true, changes: [new GitChange("a.yaml", GitFileStatus.Modified)])));

        var result = await new DiscardChanges(_git, _workspace).ExecuteAsync(["a.yaml"]);

        Assert.Equal(GitErrors.DiscardDuringMergeCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().DiscardChangesAsync(default!, default!, default);
    }

    [Fact]
    public async Task OfAFileWithoutChanges_IsRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(changes: [new GitChange("a.yaml", GitFileStatus.Modified)])));

        var result = await new DiscardChanges(_git, _workspace).ExecuteAsync(["b.yaml"]);

        Assert.Equal(GitErrors.PathsNotChangedCode, result.Error?.Code);
    }

    [Fact]
    public async Task DiscardsTheChosenChanges_AndReturnsTheNewStatus()
    {
        var change = new GitChange("a.yaml", GitFileStatus.Modified);
        var after = Clean();
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(changes: [change, new GitChange("b.yaml", GitFileStatus.Untracked)])), Ok(after));
        _git.DiscardChangesAsync(default!, default!, default).ReturnsForAnyArgs(Ok(Unit.Value));

        var result = await new DiscardChanges(_git, _workspace).ExecuteAsync(["a.yaml", "a.yaml"]);

        Assert.Same(after, result.Value);
        _ = _git.Received(1).DiscardChangesAsync(
            WorkspacePath,
            Arg.Is<IReadOnlyList<GitChange>>(c => c.SequenceEqual(new[] { change })),
            Arg.Any<CancellationToken>());
    }
}
