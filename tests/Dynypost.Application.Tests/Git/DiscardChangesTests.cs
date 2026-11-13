using Dynypost.Application.Common;
using Dynypost.Application.Git;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Git;
using NSubstitute;
using static Dynypost.Application.Tests.Git.GitTestData;

namespace Dynypost.Application.Tests.Git;

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

    [Fact]
    public async Task WhenAPathIsBothTrackedAndUntracked_UsesTheTrackedChange()
    {
        var tracked = new GitChange("same.yaml", GitFileStatus.Deleted);
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(
                Ok(Status(changes: [tracked, new GitChange("same.yaml", GitFileStatus.Untracked)])),
                Ok(Clean()));
        _git.DiscardChangesAsync(default!, default!, default).ReturnsForAnyArgs(Ok(Unit.Value));

        var result = await new DiscardChanges(_git, _workspace).ExecuteAsync(["same.yaml"]);

        Assert.True(result.IsSuccess);
        _ = _git.Received(1).DiscardChangesAsync(
            WorkspacePath,
            Arg.Is<IReadOnlyList<GitChange>>(changes => changes.SequenceEqual(new[] { tracked })),
            Arg.Any<CancellationToken>());
    }
}
