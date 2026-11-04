using Missive.Application.Settings;
using Missive.Application.Workspace;
using NSubstitute;

namespace Missive.Application.Tests.Workspace;

public class GetLastWorkspaceTests
{
    private readonly IWorkspaceRepository _workspaces = Substitute.For<IWorkspaceRepository>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly GetLastWorkspace _useCase;

    public GetLastWorkspaceTests()
    {
        _useCase = new GetLastWorkspace(_settings, _workspaces);
    }

    [Fact]
    public async Task RememberedFolderThatExists_IsReturned()
    {
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings("/work/my-api"));
        _workspaces.ExistsAsync("/work/my-api", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _useCase.ExecuteAsync();

        Assert.Equal(new WorkspaceInfo("/work/my-api", "my-api"), result.Value.Workspace);
    }

    [Fact]
    public async Task NothingRemembered_ReturnsNoWorkspace()
    {
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(AppSettings.Default);

        var result = await _useCase.ExecuteAsync();

        Assert.Null(result.Value.Workspace);
        _ = _workspaces.DidNotReceiveWithAnyArgs().ExistsAsync(default!, default);
    }

    [Fact]
    public async Task RememberedFolderThatIsGone_ReturnsNoWorkspace()
    {
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings("/work/deleted"));
        _workspaces.ExistsAsync(default!, default).ReturnsForAnyArgs(false);

        var result = await _useCase.ExecuteAsync();

        Assert.Null(result.Value.Workspace);
    }
}
