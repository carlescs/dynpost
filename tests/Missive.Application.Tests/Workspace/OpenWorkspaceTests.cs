using Missive.Application.Common;
using Missive.Application.Settings;
using Missive.Application.Workspace;
using NSubstitute;

namespace Missive.Application.Tests.Workspace;

public class OpenWorkspaceTests
{
    private const string Folder = "/work/my-api";

    private readonly IWorkspaceRepository _workspaces = Substitute.For<IWorkspaceRepository>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly OpenWorkspace _useCase;

    public OpenWorkspaceTests()
    {
        _useCase = new OpenWorkspace(_workspaces, _settings);
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(AppSettings.Default);
        _settings.SaveAsync(default!, default).ReturnsForAnyArgs(Result<Unit>.Success(Unit.Default));
    }

    [Fact]
    public async Task ExistingFolder_IsOpenedAndRemembered()
    {
        _workspaces.ExistsAsync(Folder, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _useCase.ExecuteAsync(Folder);

        Assert.True(result.IsSuccess);
        Assert.Equal(new WorkspaceInfo(Folder, "my-api"), result.Value);
        _ = _settings.Received(1).SaveAsync(
            Arg.Is<AppSettings>(s => s.LastWorkspacePath == Folder),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameFolderAgain_DoesNotRewriteTheSettings()
    {
        _workspaces.ExistsAsync(Folder, Arg.Any<CancellationToken>()).Returns(true);
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings(Folder));

        var result = await _useCase.ExecuteAsync(Folder);

        Assert.True(result.IsSuccess);
        _ = _settings.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }

    [Fact]
    public async Task FailingToRemember_StillOpensTheWorkspace()
    {
        _workspaces.ExistsAsync(Folder, Arg.Any<CancellationToken>()).Returns(true);
        _settings.SaveAsync(default!, default)
            .ReturnsForAnyArgs(Result<Unit>.Failure(new Error("settings.save_failed", "Read-only disk.")));

        var result = await _useCase.ExecuteAsync(Folder);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(Folder)]
    [InlineData("")]
    public async Task MissingFolder_IsRejectedAndNotRemembered(string path)
    {
        _workspaces.ExistsAsync(default!, default).ReturnsForAnyArgs(false);

        var result = await _useCase.ExecuteAsync(path);

        Assert.Equal(WorkspaceErrors.NotFoundCode, result.Error?.Code);
        _ = _settings.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }
}
