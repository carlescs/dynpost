using Dynypost.Application.Common;
using Dynypost.Application.Git;
using Dynypost.Domain.Git;
using NSubstitute;
using static Dynypost.Application.Tests.Git.GitTestData;

namespace Dynypost.Application.Tests.Git;

public class FetchChangesTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly FetchChanges _useCase;

    public FetchChangesTests()
    {
        _useCase = new FetchChanges(_git, OpenedWorkspace());
    }

    [Fact]
    public async Task RepositoryWithoutRemotes_IsRejected()
    {
        _git.ListRemotesAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<GitRemote>>([]));

        var result = await _useCase.ExecuteAsync();

        Assert.Equal(GitErrors.NoRemoteCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
    }

    [Fact]
    public async Task Fetch_ReturnsTheNewStatus()
    {
        var after = Status() with { Behind = 2 };
        _git.ListRemotesAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<GitRemote>>([new GitRemote("origin", "u", "u")]));
        _git.FetchAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Unit.Value));
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(after));

        var result = await _useCase.ExecuteAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Same(after, result.Value);
    }

    [Fact]
    public async Task FetchFailure_IsReturned()
    {
        _git.ListRemotesAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<GitRemote>>([new GitRemote("origin", "u", "u")]));
        _git.FetchAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Failure(GitErrors.AuthenticationFailed("denied")));

        var result = await _useCase.ExecuteAsync();

        Assert.Equal(GitErrors.AuthenticationFailedCode, result.Error?.Code);
    }
}
