using Dynpost.Application.Common;
using Dynpost.Application.Git;
using Dynpost.Domain.Git;
using NSubstitute;
using static Dynpost.Application.Tests.Git.GitTestData;

namespace Dynpost.Application.Tests.Git;

public class PushChangesTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly PushChanges _useCase;

    public PushChangesTests()
    {
        _useCase = new PushChanges(_git, OpenedWorkspace());
    }

    [Fact]
    public async Task DetachedHead_IsRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Status(branch: null)));

        var result = await _useCase.ExecuteAsync();

        Assert.Equal(GitErrors.DetachedHeadCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().PushAsync(default!, default, default);
    }

    [Fact]
    public async Task UnresolvedConflicts_AreRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(merging: true, changes: [new GitChange("a.yaml", GitFileStatus.Conflicted)])));

        var result = await _useCase.ExecuteAsync();

        Assert.Equal(GitErrors.UnresolvedConflictsCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().PushAsync(default!, default, default);
    }

    [Fact]
    public async Task BranchWithoutCommits_IsRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(upstream: null) with { IsUnborn = true }));

        var result = await _useCase.ExecuteAsync();

        Assert.Equal(GitErrors.NoCommitsCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().PushAsync(default!, default, default);
    }

    [Theory]
    [InlineData("origin/main", false)]
    [InlineData(null, true)]
    public async Task Push_SetsTheUpstreamOnlyWhenMissing(string? upstream, bool setUpstream)
    {
        var after = Status();
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Status(upstream: upstream)), Ok(after));
        _git.PushAsync(default!, default, default).ReturnsForAnyArgs(Ok(Unit.Value));

        var result = await _useCase.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Same(after, result.Value);
        _ = _git.Received(1).PushAsync(WorkspacePath, setUpstream, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectedPush_IsReturned()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Status()));
        _git.PushAsync(default!, default, default)
            .ReturnsForAnyArgs(Result<Unit>.Failure(GitErrors.PushRejected("! refs/heads/main [rejected]")));

        var result = await _useCase.ExecuteAsync();

        Assert.Equal(GitErrors.PushRejectedCode, result.Error?.Code);
    }
}
