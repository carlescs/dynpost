using Dynpost.Application.Common;
using Dynpost.Application.Git;
using Dynpost.Application.Workspaces;
using NSubstitute;
using static Dynpost.Application.Tests.Git.GitTestData;

namespace Dynpost.Application.Tests.Git;

public class InitRepositoryTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();

    [Fact]
    public async Task WithoutAWorkspace_IsReported()
    {
        var result = await new InitRepository(_git, new CurrentWorkspace()).ExecuteAsync();

        Assert.True(result.IsFailure);
        _ = _git.DidNotReceiveWithAnyArgs().InitAsync(default!, default);
    }

    [Fact]
    public async Task InitializesTheWorkspaceAndReturnsItsStatus()
    {
        var status = Status(upstream: null);
        _git.InitAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Unit.Value));
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(status));

        var result = await new InitRepository(_git, OpenedWorkspace()).ExecuteAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(status, result.Value);
    }

    [Fact]
    public async Task InitFailure_IsReturned()
    {
        _git.InitAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Failure(GitErrors.AlreadyARepository(WorkspacePath)));

        var result = await new InitRepository(_git, OpenedWorkspace()).ExecuteAsync();

        Assert.Equal(GitErrors.AlreadyARepositoryCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().GetStatusAsync(default!, default);
    }
}
