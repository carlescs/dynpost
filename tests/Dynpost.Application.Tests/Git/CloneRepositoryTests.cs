using Dynpost.Application.Common;
using Dynpost.Application.Git;
using Dynpost.Application.Settings;
using Dynpost.Application.Sharing;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests.Git;

public class CloneRepositoryTests
{
    private const string Parent = "/work";
    private static readonly string Destination = Path.Combine(Parent, "my-api");

    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly CurrentWorkspace _current = new();
    private readonly CloneRepository _clone;

    public CloneRepositoryTests()
    {
        var localFiles = Substitute.For<ILocalOnlyFiles>();
        localFiles.List(Arg.Any<string>()).Returns(new HashSet<WorkspacePath>());
        var settings = Substitute.For<IAppSettingsStore>();
        settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<AppSettings>.Success(AppSettings.Default)));
        _repository.FolderExists(Parent).Returns(true);
        _repository.FolderExists(Destination).Returns(true);
        _repository.ListAsync(Destination, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<WorkspaceEntry>>.Success([]));
        _git.CloneAsync(default!, default!, default).ReturnsForAnyArgs(Result<Unit>.Success(Unit.Value));
        var open = new OpenWorkspace(_repository, localFiles, _current, settings, Workspaces.Migrations.UpToDate());
        _clone = new CloneRepository(_git, _repository, open);
    }

    [Fact]
    public async Task ClonesIntoAFolderNamedAfterTheRepositoryAndOpensIt()
    {
        var result = await _clone.ExecuteAsync(new CloneRepositoryRequest("https://example.com/team/my-api.git", Parent));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(Destination, result.Value.Root);
        Assert.Equal(Destination, _current.Root);
        await _git.Received(1).CloneAsync(
            Arg.Is<RemoteUrl>(u => u.Value == "https://example.com/team/my-api.git"), Destination, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClonesIntoTheFolderNameGiven()
    {
        var result = await _clone.ExecuteAsync(new CloneRepositoryRequest("git@example.com:team/api.git", Parent, " my-api "));

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _git.Received(1).CloneAsync(Arg.Any<RemoteUrl>(), Destination, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidUrl_IsReported()
    {
        var result = await _clone.ExecuteAsync(new CloneRepositoryRequest("--upload-pack=evil", Parent));

        Assert.Equal(GitErrors.InvalidRemoteCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().CloneAsync(default!, default!, default);
    }

    [Fact]
    public async Task MissingParentFolder_IsReported()
    {
        var result = await _clone.ExecuteAsync(new CloneRepositoryRequest("https://example.com/my-api.git", "/nowhere"));

        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().CloneAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task InvalidFolderName_IsReported(string name)
    {
        var result = await _clone.ExecuteAsync(new CloneRepositoryRequest("https://example.com/my-api.git", Parent, name));

        Assert.Equal(GitErrors.InvalidCloneFolderCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().CloneAsync(default!, default!, default);
    }

    [Fact]
    public async Task UrlWithoutAName_AsksForOne()
    {
        var result = await _clone.ExecuteAsync(new CloneRepositoryRequest("https://", Parent));

        Assert.Equal(GitErrors.InvalidCloneFolderCode, result.Error?.Code);
    }

    [Fact]
    public async Task CloneFailure_IsReturnedAndNothingIsOpened()
    {
        _git.CloneAsync(default!, default!, default)
            .ReturnsForAnyArgs(Result<Unit>.Failure(GitErrors.CloneDestinationExists(Destination)));

        var result = await _clone.ExecuteAsync(new CloneRepositoryRequest("https://example.com/my-api.git", Parent));

        Assert.Equal(GitErrors.CloneDestinationExistsCode, result.Error?.Code);
        Assert.Null(_current.Root);
    }
}
