using Missive.Application.Common;
using Missive.Application.Git;
using Missive.Domain.Git;
using NSubstitute;
using static Missive.Application.Tests.Git.GitTestData;

namespace Missive.Application.Tests.Git;

public class PushChangesTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly PushChanges _useCase;

    public PushChangesTests()
    {
        _useCase = new PushChanges(_git);
    }

    [Fact]
    public async Task DetachedHead_IsRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Status(branch: null)));

        var result = await _useCase.ExecuteAsync(WorkspacePath);

        Assert.Equal(GitErrors.DetachedHeadCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().PushAsync(default!, default, default);
    }

    [Fact]
    public async Task UnresolvedConflicts_AreRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(merging: true, changes: [new GitChange("a.yaml", GitFileStatus.Conflicted)])));

        var result = await _useCase.ExecuteAsync(WorkspacePath);

        Assert.Equal(GitErrors.UnresolvedConflictsCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().PushAsync(default!, default, default);
    }

    [Theory]
    [InlineData("origin/main", false)]
    [InlineData(null, true)]
    public async Task Push_SetsTheUpstreamOnlyWhenMissing(string? upstream, bool setUpstream)
    {
        var after = Status();
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Status(upstream: upstream)), Ok(after));
        _git.PushAsync(default!, default, default).ReturnsForAnyArgs(Ok(Unit.Default));

        var result = await _useCase.ExecuteAsync(WorkspacePath);

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

        var result = await _useCase.ExecuteAsync(WorkspacePath);

        Assert.Equal(GitErrors.PushRejectedCode, result.Error?.Code);
    }
}
