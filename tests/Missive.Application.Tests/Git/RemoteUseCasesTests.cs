using Missive.Application.Common;
using Missive.Application.Git;
using Missive.Application.Workspaces;
using Missive.Domain.Git;
using NSubstitute;
using static Missive.Application.Tests.Git.GitTestData;

namespace Missive.Application.Tests.Git;

public class RemoteUseCasesTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly CurrentWorkspace _workspace = OpenedWorkspace();

    private static readonly GitRemote Origin = new("origin", "https://example.com/a.git", "https://example.com/a.git");

    private void HasRemotes(params IReadOnlyList<GitRemote>[] lists)
    {
        var results = lists.Select(l => Ok(l)).ToArray();
        _git.ListRemotesAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(results[0], results[1..]);
    }

    [Fact]
    public async Task List_WithoutAWorkspace_IsRejected()
    {
        var result = await new ListRemotes(_git, new CurrentWorkspace()).ExecuteAsync();

        Assert.Equal(ErrorCodes.NoWorkspace, result.Error?.Code);
    }

    [Fact]
    public async Task Add_AddsTheRemote_AndReturnsTheNewList()
    {
        var after = new[] { Origin };
        HasRemotes([], after);
        _git.AddRemoteAsync(default!, default!, default!).ReturnsForAnyArgs(Ok(Unit.Value));

        var result = await new AddRemote(_git, _workspace).ExecuteAsync(new AddRemoteRequest(" origin ", " https://example.com/a.git "));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Same(after, result.Value);
        _ = _git.Received(1).AddRemoteAsync(
            WorkspacePath,
            Arg.Is<RemoteName>(n => n.Value == "origin"),
            Arg.Is<RemoteUrl>(u => u.Value == "https://example.com/a.git"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("-bad", "https://a")]
    [InlineData("origin", "")]
    [InlineData("origin", "--upload-pack=x")]
    public async Task Add_InvalidNameOrUrl_IsRejected(string name, string url)
    {
        var result = await new AddRemote(_git, _workspace).ExecuteAsync(new AddRemoteRequest(name, url));

        Assert.Equal(GitErrors.InvalidRemoteCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().AddRemoteAsync(default!, default!, default!);
    }

    [Fact]
    public async Task Add_AnExistingName_IsRejected()
    {
        HasRemotes([Origin]);

        var result = await new AddRemote(_git, _workspace).ExecuteAsync(new AddRemoteRequest("origin", "https://b"));

        Assert.Equal(GitErrors.RemoteExistsCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().AddRemoteAsync(default!, default!, default!);
    }

    [Fact]
    public async Task Update_RenamesAndChangesTheUrl()
    {
        HasRemotes([Origin], []);
        _git.RenameRemoteAsync(default!, default!, default!).ReturnsForAnyArgs(Ok(Unit.Value));
        _git.SetRemoteUrlAsync(default!, default!, default!).ReturnsForAnyArgs(Ok(Unit.Value));

        var result = await new UpdateRemote(_git, _workspace)
            .ExecuteAsync(new UpdateRemoteRequest("origin", "upstream", "https://new"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Received.InOrder(() =>
        {
            _git.RenameRemoteAsync(WorkspacePath, "origin", Arg.Is<RemoteName>(n => n.Value == "upstream"), Arg.Any<CancellationToken>());
            _git.SetRemoteUrlAsync(WorkspacePath, "upstream", Arg.Is<RemoteUrl>(u => u.Value == "https://new"), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Update_WithNothingChanged_RunsNoGitCommand()
    {
        HasRemotes([Origin], [Origin]);

        var result = await new UpdateRemote(_git, _workspace)
            .ExecuteAsync(new UpdateRemoteRequest("origin", "origin", Origin.FetchUrl));

        Assert.True(result.IsSuccess, result.Error?.Message);
        _ = _git.DidNotReceiveWithAnyArgs().RenameRemoteAsync(default!, default!, default!);
        _ = _git.DidNotReceiveWithAnyArgs().SetRemoteUrlAsync(default!, default!, default!);
    }

    [Fact]
    public async Task Update_ToATakenName_IsRejected()
    {
        HasRemotes([Origin, Origin with { Name = "backup" }]);

        var result = await new UpdateRemote(_git, _workspace)
            .ExecuteAsync(new UpdateRemoteRequest("origin", "backup", Origin.FetchUrl));

        Assert.Equal(GitErrors.RemoteExistsCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().RenameRemoteAsync(default!, default!, default!);
    }

    [Fact]
    public async Task Update_AnUnknownRemote_IsRejected()
    {
        HasRemotes([Origin]);

        var result = await new UpdateRemote(_git, _workspace)
            .ExecuteAsync(new UpdateRemoteRequest("nope", "nope", "https://a"));

        Assert.Equal(GitErrors.RemoteNotFoundCode, result.Error?.Code);
    }

    [Fact]
    public async Task Remove_RemovesAnExistingRemote()
    {
        HasRemotes([Origin], []);
        _git.RemoveRemoteAsync(default!, default!).ReturnsForAnyArgs(Ok(Unit.Value));

        var result = await new RemoveRemote(_git, _workspace).ExecuteAsync("origin");

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Empty(result.Value);
        _ = _git.Received(1).RemoveRemoteAsync(WorkspacePath, "origin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_AnUnknownRemote_IsRejected()
    {
        HasRemotes([Origin]);

        var result = await new RemoveRemote(_git, _workspace).ExecuteAsync("--all");

        Assert.Equal(GitErrors.RemoteNotFoundCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().RemoveRemoteAsync(default!, default!);
    }
}
