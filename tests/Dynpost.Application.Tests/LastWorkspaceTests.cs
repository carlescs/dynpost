using Dynpost.Application.Common;
using Dynpost.Application.Sharing;
using Dynpost.Application.Settings;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests;

public class LastWorkspaceTests
{
    private const string Folder = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
    private readonly CurrentWorkspace _current = new();
    private readonly OpenWorkspace _open;
    private AppSettings _storedSettings = AppSettings.Default;

    public LastWorkspaceTests()
    {
        _localFiles.List(Arg.Any<string>()).Returns(new HashSet<WorkspacePath>());
        _open = new OpenWorkspace(_repository, _localFiles, _current, _settings, Workspaces.Migrations.UpToDate());
        _repository.ListAsync(default!, default)
            .ReturnsForAnyArgs(Result<IReadOnlyList<WorkspaceEntry>>.Success(Array.Empty<WorkspaceEntry>()));
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => _storedSettings);
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _storedSettings = call.Arg<Func<AppSettings, AppSettings>>()(_storedSettings);
                return Task.FromResult<Result<AppSettings>>(_storedSettings);
            });
    }

    [Fact]
    public async Task OpeningAFolder_RemembersIt()
    {
        _repository.FolderExists(Folder).Returns(true);

        await _open.ExecuteAsync(Folder);

        Assert.Equal(Folder, _storedSettings.LastWorkspacePath);
        _ = _settings.Received(1).UpdateAsync(
            Arg.Any<Func<AppSettings, AppSettings>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpeningTheSameFolderAgain_KeepsSettingsUnchanged()
    {
        _repository.FolderExists(Folder).Returns(true);
        var settings = new AppSettings(Folder, RecentWorkspaces: [Folder]);
        _storedSettings = settings;

        await _open.ExecuteAsync(Folder);

        Assert.Equal(settings, _storedSettings);
    }

    [Fact]
    public async Task OpeningAFolder_MovesItToTheTopOfTheRecentWorkspaces()
    {
        _repository.FolderExists(Folder).Returns(true);
        _storedSettings = new AppSettings("/work/other", RecentWorkspaces: ["/work/other", Folder, "/work/third"]);

        await _open.ExecuteAsync(Folder);

        Assert.Equal([Folder, "/work/other", "/work/third"], _storedSettings.RecentWorkspaces);
    }

    [Fact]
    public void RecentWorkspaces_KeepAtMostTheMaximum()
    {
        var recent = Enumerable.Range(1, RecentWorkspaces.MaxCount).Select(i => $"/work/{i}").ToList();

        var updated = RecentWorkspaces.Add(recent, "/work/new");

        Assert.Equal(RecentWorkspaces.MaxCount, updated.Count);
        Assert.Equal("/work/new", updated[0]);
        Assert.DoesNotContain($"/work/{RecentWorkspaces.MaxCount}", updated);
    }

    [Fact]
    public async Task GetRecent_ListsExistingFoldersAndForgetsMissingOnes()
    {
        _storedSettings = new AppSettings(Folder, RecentWorkspaces: [Folder, "/work/gone", "/work/other/"]);
        _repository.FolderExists(Folder).Returns(true);
        _repository.FolderExists("/work/other/").Returns(true);

        var result = await new GetRecentWorkspaces(_settings, _repository).ExecuteAsync();

        Assert.Equal(
            [new RecentWorkspaceDto("my-api", Folder), new RecentWorkspaceDto("other", "/work/other/")],
            result.Value);
        Assert.Equal([Folder, "/work/other/"], _storedSettings.RecentWorkspaces);
    }

    [Fact]
    public async Task GetRecent_KeepsFoldersOpenedWhileItChecked()
    {
        _storedSettings = new AppSettings(Folder, RecentWorkspaces: [Folder, "/work/gone"]);
        _repository.FolderExists(Folder).Returns(true);
        // Another workspace is opened between reading the list and saving it.
        _repository.FolderExists("/work/gone").Returns(_ =>
        {
            _storedSettings = _storedSettings with { RecentWorkspaces = ["/work/new", .. _storedSettings.RecentWorkspaces!] };
            return false;
        });

        await new GetRecentWorkspaces(_settings, _repository).ExecuteAsync();

        Assert.Equal(["/work/new", Folder], _storedSettings.RecentWorkspaces);
    }

    [Fact]
    public async Task GetRecent_DoesNotRewriteSettingsWhenAllFoldersExist()
    {
        _storedSettings = new AppSettings(Folder, RecentWorkspaces: [Folder]);
        _repository.FolderExists(Folder).Returns(true);

        var result = await new GetRecentWorkspaces(_settings, _repository).ExecuteAsync();

        Assert.Single(result.Value);
        _ = _settings.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task RemoveRecent_RemovesOnlyTheSelectedFolderAndKeepsLastWorkspace()
    {
        _storedSettings = new AppSettings(Folder, RecentWorkspaces: [Folder, "/work/other"]);

        var result = await new RemoveRecentWorkspace(_settings).ExecuteAsync("/work/other");

        Assert.True(result.IsSuccess);
        Assert.Equal(Folder, _storedSettings.LastWorkspacePath);
        Assert.Equal([Folder], _storedSettings.RecentWorkspaces);
    }

    [Fact]
    public async Task RemoveRecent_WhenFolderIsNotListed_KeepsSettingsUnchanged()
    {
        _storedSettings = new AppSettings(Folder, RecentWorkspaces: [Folder]);
        var settings = _storedSettings;

        var result = await new RemoveRecentWorkspace(_settings).ExecuteAsync("/work/other");

        Assert.True(result.IsSuccess);
        Assert.Same(settings, _storedSettings);
    }

    [Fact]
    public async Task FailingToRemember_StillOpensTheFolder()
    {
        _repository.FolderExists(Folder).Returns(true);
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<AppSettings>.Failure("settings.save_failed", "Read-only disk.")));

        var result = await _open.ExecuteAsync(Folder);

        Assert.True(result.IsSuccess);
        Assert.Equal(Folder, _current.Root);
    }

    [Fact]
    public async Task OpenLast_ReopensTheRememberedFolder()
    {
        _storedSettings = new AppSettings(Folder);
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
        _storedSettings = new AppSettings(remembered);
        _repository.FolderExists(default!).ReturnsForAnyArgs(false);

        var result = await new OpenLastWorkspace(_settings, _repository, _open).ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Null(_current.Root);
    }
}
