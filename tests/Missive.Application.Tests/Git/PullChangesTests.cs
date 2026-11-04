using Missive.Application.Common;
using Missive.Application.Git;
using Missive.Domain.Git;
using NSubstitute;
using static Missive.Application.Tests.Git.GitTestData;

namespace Missive.Application.Tests.Git;

public class PullChangesTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly PullChanges _useCase;

    public PullChangesTests()
    {
        _useCase = new PullChanges(_git, OpenedWorkspace());
    }

    [Fact]
    public async Task DetachedHead_IsRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Status(branch: null, upstream: null)));

        var result = await _useCase.ExecuteAsync();

        Assert.Equal(GitErrors.DetachedHeadCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().PullAsync(default!, default);
    }

    [Fact]
    public async Task BranchWithoutUpstream_IsRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Status(upstream: null)));

        var result = await _useCase.ExecuteAsync();

        Assert.Equal(GitErrors.NoUpstreamCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().PullAsync(default!, default);
    }

    [Fact]
    public async Task UnresolvedConflicts_AreRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(merging: true, changes: [new GitChange("a.yaml", GitFileStatus.Conflicted)])));

        var result = await _useCase.ExecuteAsync();

        Assert.Equal(GitErrors.UnresolvedConflictsCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().PullAsync(default!, default);
    }

    [Theory]
    [InlineData(PullOutcome.UpToDate)]
    [InlineData(PullOutcome.Updated)]
    [InlineData(PullOutcome.Conflicted)]
    public async Task Pull_ReturnsTheOutcomeAndTheNewStatus(PullOutcome outcome)
    {
        var after = Status(merging: true, changes: [new GitChange("a.yaml", GitFileStatus.Conflicted)]);
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Clean()), Ok(after));
        _git.PullAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(outcome));

        var result = await _useCase.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(outcome, result.Value.Outcome);
        Assert.Same(after, result.Value.Status);
    }

    [Fact]
    public async Task PullFailure_IsReturned()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Clean()));
        _git.PullAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Result<PullOutcome>.Failure(GitErrors.CommandFailed("pull", "offline")));

        var result = await _useCase.ExecuteAsync();

        Assert.Equal(GitErrors.CommandFailedCode, result.Error?.Code);
        Assert.Contains("offline", result.Error!.Message);
    }
}
