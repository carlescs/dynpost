using Dynypost.Application.Common;
using Dynypost.Application.Git;
using Dynypost.Domain.Git;
using NSubstitute;
using static Dynypost.Application.Tests.Git.GitTestData;

namespace Dynypost.Application.Tests.Git;

public class IdentityUseCasesTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();

    [Fact]
    public async Task Get_ReturnsTheConfiguredIdentity()
    {
        var identity = new GitIdentitySettings("Ada", null);
        _git.GetIdentityAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(identity));

        var result = await new GetGitIdentity(_git, OpenedWorkspace()).ExecuteAsync();

        Assert.Same(identity, result.Value);
        Assert.False(result.Value.IsComplete);
    }

    [Theory]
    [InlineData("", "ada@example.com")]
    [InlineData("Ada", "")]
    [InlineData("Ada", "not-an-email")]
    [InlineData("Ada <x>", "ada@example.com")]
    [InlineData("Ada\nLovelace", "ada@example.com")]
    public async Task Set_InvalidIdentity_IsRejected(string name, string email)
    {
        var result = await new SetGitIdentity(_git, OpenedWorkspace())
            .ExecuteAsync(new SetGitIdentityRequest(name, email));

        Assert.Equal(GitErrors.InvalidIdentityCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().SetIdentityAsync(default!, default!, default, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Set_StoresTheTrimmedIdentity_AndReturnsIt(bool global)
    {
        var stored = new GitIdentitySettings("Ada", "ada@example.com");
        _git.SetIdentityAsync(default!, default!, default, default).ReturnsForAnyArgs(Ok(Unit.Value));
        _git.GetIdentityAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(stored));

        var result = await new SetGitIdentity(_git, OpenedWorkspace())
            .ExecuteAsync(new SetGitIdentityRequest(" Ada ", " ada@example.com ", global));

        Assert.Same(stored, result.Value);
        _ = _git.Received(1).SetIdentityAsync(
            WorkspacePath,
            Arg.Is<GitIdentity>(i => i.Name == "Ada" && i.Email == "ada@example.com"),
            global,
            Arg.Any<CancellationToken>());
    }
}
