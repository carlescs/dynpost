using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Dynpost.Application.Common;
using Dynpost.Application.Folders;
using Dynpost.Application.Git;
using Dynpost.Application.Requests;
using Dynpost.Application.Settings;
using Dynpost.Application.Workspaces;
using Dynpost.Application.Sharing;
using Dynpost.Desktop.Bridge;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Desktop.Tests.Bridge;

/// <summary>Duplicating requests and folders and listing and showing workspaces, through the real router and use cases.</summary>
public sealed class WorkspaceBridgeTests : IDisposable
{
    private const string Root = "/work/my-api";

    private readonly ILocalOnlyFiles _localFiles = CreateLocalFiles();
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly IFileManager _fileManager = Substitute.For<IFileManager>();
    private readonly IFolderDefaultsStore _folders = Substitute.For<IFolderDefaultsStore>();
    private readonly ServiceProvider _services;
    private readonly BridgeRouter _router;

    public WorkspaceBridgeTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(_repository)
            .AddSingleton(_localFiles)
            .AddSingleton(_settings)
            .AddSingleton(_fileManager)
            .AddSingleton(_folders)
            .AddSingleton<FolderInheritance>()
            .AddTransient<LoadFolderSettings>()
            .AddTransient<SaveFolderSettings>()
            .AddTransient<GetInheritedSettings>()
            .AddSingleton<CurrentWorkspace>()
            .AddSingleton(_git)
            .AddTransient<DuplicateRequest>()
            .AddTransient<SetLocalOnly>()
            .AddTransient<DuplicateFolder>()
            .AddTransient<GetRecentWorkspaces>()
            .AddTransient<RemoveRecentWorkspace>()
            .AddTransient<ShowWorkspaceFolder>()
            .BuildServiceProvider();
        _services.GetRequiredService<CurrentWorkspace>().Open(Root);
        _router = BridgeHandlers.Create(_services, Substitute.For<IFileDialogs>(), Substitute.For<IAppWindow>());
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task DuplicateRequest_ReturnsTheCopy()
    {
        var path = WorkspacePath.Parse("requests/get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);
        _repository.ReadRequestAsync(Root, path, Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get user", RequestMethod.Get, "/users/1"));
        _repository.WriteRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var response = await SendAsync("request.duplicate", """{ "path": "requests/get-user.yaml" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("requests/get-user-copy.yaml", response.GetProperty("payload").GetProperty("path").GetString());
        Assert.Equal("Get user copy", response.GetProperty("payload").GetProperty("request").GetProperty("name").GetString());
    }

    [Fact]
    public async Task FolderSettings_AreSavedAndPassedOnToSubFolders()
    {
        var users = WorkspacePath.Parse("requests/users");
        FolderDefaults? saved = null;
        _repository.DirectoryExists(Root, Arg.Any<WorkspacePath>()).Returns(true);
        _folders.ReadAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<WorkspacePath>() == users && saved is not null ? saved : FolderDefaults.Empty);
        _folders.WriteAsync(Root, users, Arg.Do<FolderDefaults>(d => saved = d), Arg.Any<CancellationToken>()).Returns(true);

        var save = await SendAsync("folder.settings.save", """
            { "path": "requests/users", "settings": {
                "headers": [{ "key": "Accept", "value": "application/json", "enabled": true }],
                "auth": { "type": "bearer", "token": "{{token}}" } } }
            """);
        var inherited = await SendAsync("folder.settings.inherited", """{ "path": "requests/users/admin" }""");

        Assert.True(save.GetProperty("ok").GetBoolean(), save.ToString());
        Assert.Equal("bearer", save.GetProperty("payload").GetProperty("auth").GetProperty("type").GetString());
        Assert.True(inherited.GetProperty("ok").GetBoolean(), inherited.ToString());
        var payload = inherited.GetProperty("payload");
        Assert.Equal("requests/users", payload.GetProperty("authFolder").GetString());
        Assert.Equal("{{token}}", payload.GetProperty("auth").GetProperty("token").GetString());
        var header = Assert.Single(payload.GetProperty("headers").EnumerateArray());
        Assert.Equal("Accept", header.GetProperty("key").GetString());
        Assert.Equal("requests/users", header.GetProperty("folder").GetString());

        var load = await SendAsync("folder.settings.load", """{ "path": "requests/users" }""");
        Assert.Equal("application/json", load.GetProperty("payload").GetProperty("headers")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task DuplicateFolder_ReturnsTheCopysPath()
    {
        _repository.DirectoryExists(Root, WorkspacePath.Parse("requests/users")).Returns(true);
        _repository.ReadFolderContents(Root, WorkspacePath.Parse("requests/users")).Returns(new FolderContents([], [], []));
        _repository.CreateFolder(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);

        var response = await SendAsync("folder.duplicate", """{ "path": "requests/users" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("requests/users copy", response.GetProperty("payload").GetProperty("path").GetString());
    }

    [Fact]
    public async Task Recent_ListsTheRememberedFolders()
    {
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings(Root, RecentWorkspaces: [Root]));
        _repository.FolderExists(Root).Returns(true);

        var response = await SendAsync("workspace.recent", "null");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var entry = Assert.Single(response.GetProperty("payload").EnumerateArray());
        Assert.Equal("my-api", entry.GetProperty("name").GetString());
        Assert.Equal(Root, entry.GetProperty("path").GetString());
    }

    [Fact]
    public async Task RecentRemove_RemovesTheSelectedFolder()
    {
        var settings = new AppSettings(Root, RecentWorkspaces: [Root, "/work/other"]);
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(settings);
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Result<AppSettings>>(
                call.Arg<Func<AppSettings, AppSettings>>()(settings)));

        var response = await SendAsync("workspace.recent.remove", """{ "path": "/work/other" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Null(response.GetProperty("payload").GetString());
        _ = _settings.Received(1).UpdateAsync(
            Arg.Is<Func<AppSettings, AppSettings>>(update =>
                update(settings).RecentWorkspaces!.SequenceEqual(new[] { Root })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reveal_ShowsTheWorkspaceFolderInTheFileManager()
    {
        _repository.FolderExists(Root).Returns(true);
        _fileManager.ShowFolder(Root).Returns(Unit.Value);

        var response = await SendAsync("workspace.reveal", "null");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        _fileManager.Received(1).ShowFolder(Root);
    }

    [Fact]
    public async Task SharingSet_MakesTheFileLocalOnly()
    {
        var path = WorkspacePath.Parse("environments/local.yaml");
        _repository.FileExists(Root, path).Returns(true);
        _localFiles.Set(Root, path, true).Returns(true);
        _git.StopTrackingAsync(Root, "environments/local.yaml", Arg.Any<CancellationToken>()).Returns(Unit.Value);

        var response = await SendAsync("sharing.set", """{ "path": "environments/local.yaml", "localOnly": true }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.True(response.GetProperty("payload").GetProperty("localOnly").GetBoolean());
        _localFiles.Received(1).Set(Root, path, true);
    }

    private async Task<JsonElement> SendAsync(string type, string payload)
    {
        var json = await _router.HandleAsync($$"""{ "id": "1", "type": "{{type}}", "payload": {{payload}} }""");
        Assert.NotNull(json);
        return JsonDocument.Parse(json).RootElement;
    }

    private static ILocalOnlyFiles CreateLocalFiles()
    {
        var files = Substitute.For<ILocalOnlyFiles>();
        files.List(Arg.Any<string>()).Returns(new HashSet<WorkspacePath>());
        return files;
    }
}
