using Missive.Application.Common;
using Missive.Application.Settings;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests;

public class LastWorkspaceTests
{
    private const string Folder = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly CurrentWorkspace _current = new();
    private readonly OpenWorkspace _open;

    public LastWorkspaceTests()
    {
        _open = new OpenWorkspace(_repository, _current, _settings);
        _repository.ListAsync(default!, default)
            .ReturnsForAnyArgs(Result<IReadOnlyList<WorkspaceEntry>>.Success(Array.Empty<WorkspaceEntry>()));
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(AppSettings.Default);
        _settings.SaveAsync(default!, default).ReturnsForAnyArgs(Result<Unit>.Success(Unit.Value));
    }

    [Fact]
    public async Task OpeningAFolder_RemembersIt()
    {
        _repository.FolderExists(Folder).Returns(true);

        await _open.ExecuteAsync(Folder);

        _ = _settings.Received(1).SaveAsync(
            Arg.Is<AppSettings>(s => s.LastWorkspacePath == Folder),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpeningTheSameFolderAgain_DoesNotRewriteTheSettings()
    {
        _repository.FolderExists(Folder).Returns(true);
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings(Folder));

        await _open.ExecuteAsync(Folder);

        _ = _settings.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }

    [Fact]
    public async Task FailingToRemember_StillOpensTheFolder()
    {
        _repository.FolderExists(Folder).Returns(true);
        _settings.SaveAsync(default!, default)
            .ReturnsForAnyArgs(Result<Unit>.Failure("settings.save_failed", "Read-only disk."));

        var result = await _open.ExecuteAsync(Folder);

        Assert.True(result.IsSuccess);
        Assert.Equal(Folder, _current.Root);
    }

    [Fact]
    public async Task OpenLast_ReopensTheRememberedFolder()
    {
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings(Folder));
        _repository.FolderExists(Folder).Returns(true);

        var result = await new OpenLastWorkspace(_settings, _repository, _open).ExecuteAsync();

        Assert.Equal("my-api", result.Value?.Name);
        Assert.Equal(Folder, _current.Root);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/work/deleted")]
    public async Task OpenLast_WithNothingToReopen_ReturnsNull(string? remembered)
    {
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings(remembered));
        _repository.FolderExists(default!).ReturnsForAnyArgs(false);

        var result = await new OpenLastWorkspace(_settings, _repository, _open).ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Null(_current.Root);
    }
}
