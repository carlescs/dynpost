using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Common;
using Missive.Application.Folders;
using Missive.Application.Requests;
using Missive.Application.Settings;
using Missive.Application.Workspaces;
using Missive.Desktop.Bridge;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Desktop.Tests.Bridge;

/// <summary>Duplicating requests and folders and listing recent workspaces, through the real router and use cases.</summary>
public sealed class WorkspaceBridgeTests : IDisposable
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly ServiceProvider _services;
    private readonly BridgeRouter _router;

    public WorkspaceBridgeTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(_repository)
            .AddSingleton(_settings)
            .AddSingleton<CurrentWorkspace>()
            .AddTransient<DuplicateRequest>()
            .AddTransient<DuplicateFolder>()
            .AddTransient<GetRecentWorkspaces>()
            .BuildServiceProvider();
        _services.GetRequiredService<CurrentWorkspace>().Open(Root);
        _router = BridgeHandlers.Create(_services, Substitute.For<IFileDialogs>());
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task DuplicateRequest_ReturnsTheCopy()
    {
        var path = WorkspacePath.Parse("get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);
        _repository.ReadRequestAsync(Root, path, Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get user", RequestMethod.Get, "/users/1"));
        _repository.WriteRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        var response = await SendAsync("request.duplicate", """{ "path": "get-user.yaml" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("get-user-copy.yaml", response.GetProperty("payload").GetProperty("path").GetString());
        Assert.Equal("Get user copy", response.GetProperty("payload").GetProperty("request").GetProperty("name").GetString());
    }

    [Fact]
    public async Task DuplicateFolder_ReturnsTheCopysPath()
    {
        _repository.DirectoryExists(Root, WorkspacePath.Parse("users")).Returns(true);
        _repository.ReadFolderContents(Root, WorkspacePath.Parse("users")).Returns(new FolderContents([], [], []));
        _repository.CreateFolder(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);

        var response = await SendAsync("folder.duplicate", """{ "path": "users" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("users copy", response.GetProperty("payload").GetProperty("path").GetString());
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

    private async Task<JsonElement> SendAsync(string type, string payload)
    {
        var json = await _router.HandleAsync($$"""{ "id": "1", "type": "{{type}}", "payload": {{payload}} }""");
        Assert.NotNull(json);
        return JsonDocument.Parse(json).RootElement;
    }
}
