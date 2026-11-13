using Dynpost.Application.Common;
using Dynpost.Application.Git;
using Dynpost.Application.Sharing;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests.Sharing;

public sealed class SetLocalOnlyTests
{
    private const string Root = "/work/my-api";

    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly CurrentWorkspace _current = new();

    public SetLocalOnlyTests()
    {
        _current.Open(Root);
        _repository.FileExists(Root, Arg.Any<WorkspacePath>()).Returns(true);
        _localFiles.Set(Root, Arg.Any<WorkspacePath>(), Arg.Any<bool>()).Returns(true);
        _git.StopTrackingAsync(Root, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _git.RestoreTrackingAsync(Root, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
    }

    private SetLocalOnly UseCase() => new(_localFiles, _repository, _git, _current);

    [Theory]
    [InlineData("requests/users/get-user.yaml")]
    [InlineData("environments/local.yaml")]
    [InlineData("plans/load/smoke.run.yaml")]
    public async Task MakingAFileLocalOnly_IgnoresItAndStopsTrackingIt(string path)
    {
        var result = await UseCase().ExecuteAsync(path, localOnly: true);

        Assert.Equal(new SharingDto(path, true), result.Value);
        _localFiles.Received(1).Set(Root, WorkspacePath.Parse(path), true);
        await _git.Received(1).StopTrackingAsync(Root, path, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SharingAFile_RemovesTheEntryAndRestoresTracking()
    {
        var result = await UseCase().ExecuteAsync("environments/dev.yaml", localOnly: false);

        Assert.Equal(new SharingDto("environments/dev.yaml", false), result.Value);
        _localFiles.Received(1).Set(Root, WorkspacePath.Parse("environments/dev.yaml"), false);
        await _git.Received(1).RestoreTrackingAsync(Root, "environments/dev.yaml", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(GitErrors.NotARepositoryCode)]
    [InlineData(GitErrors.GitNotInstalledCode)]
    public async Task WorksWithoutGit(string code)
    {
        _git.StopTrackingAsync(Root, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new Error(code, "no git"));

        var result = await UseCase().ExecuteAsync("requests/users/get-user.yaml", localOnly: true);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ReportsOtherGitFailures()
    {
        _git.StopTrackingAsync(Root, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Error(GitErrors.RepositoryLockedCode, "locked"));

        var result = await UseCase().ExecuteAsync("requests/users/get-user.yaml", localOnly: true);

        Assert.Equal(GitErrors.RepositoryLockedCode, result.Error?.Code);
    }

    [Theory]
    [InlineData("dynpost.yaml")]
    [InlineData(".gitignore")]
    [InlineData("requests/users/notes.txt")]
    [InlineData("requests/users/_folder.yaml")]
    [InlineData("users/get-user.yaml")]
    [InlineData("requests/load/smoke.run.yaml")]
    [InlineData(".dynpost/secrets.yaml")]
    [InlineData("environments/nested/dev.yaml")]
    [InlineData("../outside.yaml")]
    [InlineData("")]
    public async Task RefusesFilesThatAreNotRequestsEnvironmentsOrRunPlans(string path)
    {
        var result = await UseCase().ExecuteAsync(path, localOnly: true);

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
        _localFiles.DidNotReceiveWithAnyArgs().Set(default!, default!, default);
    }

    [Fact]
    public async Task RefusesMissingFiles()
    {
        _repository.FileExists(Root, WorkspacePath.Parse("requests/users/gone.yaml")).Returns(false);

        var result = await UseCase().ExecuteAsync("requests/users/gone.yaml", localOnly: true);

        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
    }

    [Fact]
    public async Task RequiresAnOpenWorkspace()
    {
        var result = await new SetLocalOnly(_localFiles, _repository, _git, new CurrentWorkspace())
            .ExecuteAsync("requests/users/get-user.yaml", localOnly: true);

        Assert.Equal(ErrorCodes.NoWorkspace, result.Error?.Code);
    }
}
