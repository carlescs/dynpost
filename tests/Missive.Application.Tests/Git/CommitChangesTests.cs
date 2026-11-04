using Missive.Application.Common;
using Missive.Application.Git;
using Missive.Domain.Git;
using NSubstitute;
using static Missive.Application.Tests.Git.GitTestData;

namespace Missive.Application.Tests.Git;

public class CommitChangesTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly CommitChanges _useCase;

    public CommitChangesTests()
    {
        _useCase = new CommitChanges(_git);
    }

    [Fact]
    public async Task BlankMessage_FailsWithoutTouchingGit()
    {
        var result = await _useCase.ExecuteAsync(new CommitChangesRequest(WorkspacePath, "  "));

        Assert.Equal(GitErrors.InvalidCommitMessageCode, result.Error?.Code);
        Assert.Empty(_git.ReceivedCalls());
    }

    [Fact]
    public async Task StatusFailure_IsReturned()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Result<GitStatus>.Failure(GitErrors.NotARepository(WorkspacePath)));

        var result = await _useCase.ExecuteAsync(new CommitChangesRequest(WorkspacePath, "Add users"));

        Assert.Equal(GitErrors.NotARepositoryCode, result.Error?.Code);
    }

    [Fact]
    public async Task NoChanges_FailsWithNothingToCommit()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Clean()));

        var result = await _useCase.ExecuteAsync(new CommitChangesRequest(WorkspacePath, "Add users"));

        Assert.Equal(GitErrors.NothingToCommitCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().CommitAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Conflicts_BlockTheCommit()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(merging: true, changes: [new GitChange("users.yaml", GitFileStatus.Conflicted)])));

        var result = await _useCase.ExecuteAsync(new CommitChangesRequest(WorkspacePath, "Merge"));

        Assert.Equal(GitErrors.UnresolvedConflictsCode, result.Error?.Code);
        Assert.Contains("users.yaml", result.Error!.Message);
        _ = _git.DidNotReceiveWithAnyArgs().CommitAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task UnchangedPaths_AreRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(changes: [new GitChange("a.yaml", GitFileStatus.Modified)])));

        var result = await _useCase.ExecuteAsync(new CommitChangesRequest(WorkspacePath, "Update", ["a.yaml", "b.yaml"]));

        Assert.Equal(GitErrors.PathsNotChangedCode, result.Error?.Code);
        Assert.Contains("b.yaml", result.Error!.Message);
        _ = _git.DidNotReceiveWithAnyArgs().CommitAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task SelectedPathsDuringMerge_AreRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(merging: true, changes: [new GitChange("a.yaml", GitFileStatus.Modified)])));

        var result = await _useCase.ExecuteAsync(new CommitChangesRequest(WorkspacePath, "Merge", ["a.yaml"]));

        Assert.Equal(GitErrors.PartialCommitDuringMergeCode, result.Error?.Code);
    }

    [Fact]
    public async Task CommitsEverything_AndReturnsTheNewStatus()
    {
        var after = Clean();
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(changes: [new GitChange("a.yaml", GitFileStatus.Modified)])), Ok(after));
        _git.CommitAsync(default!, default!, default, default).ReturnsForAnyArgs(Ok("abc123"));

        var result = await _useCase.ExecuteAsync(new CommitChangesRequest(WorkspacePath, "  Update a\r\n"));

        Assert.True(result.IsSuccess);
        Assert.Equal("abc123", result.Value.CommitId);
        Assert.Same(after, result.Value.Status);
        _ = _git.Received(1).CommitAsync(
            WorkspacePath,
            Arg.Is<CommitMessage>(m => m.Value == "Update a"),
            Arg.Is<IReadOnlyList<string>?>(p => p == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SelectedRename_AlsoCommitsItsOriginalPath()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(changes:
            [
                new GitChange("new.yaml", GitFileStatus.Renamed, "old.yaml"),
                new GitChange("other.yaml", GitFileStatus.Modified),
            ])));
        _git.CommitAsync(default!, default!, default, default).ReturnsForAnyArgs(Ok("abc123"));

        var result = await _useCase.ExecuteAsync(new CommitChangesRequest(WorkspacePath, "Rename", ["new.yaml"]));

        Assert.True(result.IsSuccess);
        _ = _git.Received(1).CommitAsync(
            WorkspacePath,
            Arg.Any<CommitMessage>(),
            Arg.Is<IReadOnlyList<string>>(p => p.SequenceEqual(new[] { "new.yaml", "old.yaml" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitFailure_IsReturned()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(changes: [new GitChange("a.yaml", GitFileStatus.Modified)])));
        _git.CommitAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(Result<string>.Failure(GitErrors.CommandFailed("commit", "hook failed")));

        var result = await _useCase.ExecuteAsync(new CommitChangesRequest(WorkspacePath, "Update"));

        Assert.Equal(GitErrors.CommandFailedCode, result.Error?.Code);
    }
}
