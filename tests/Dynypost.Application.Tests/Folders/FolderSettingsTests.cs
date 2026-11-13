using Dynypost.Application.Common;
using Dynypost.Application.Folders;
using Dynypost.Application.Requests;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Requests;
using Dynypost.Domain.Workspaces;
using NSubstitute;

namespace Dynypost.Application.Tests.Folders;

public class FolderSettingsTests
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IFolderDefaultsStore _store = Substitute.For<IFolderDefaultsStore>();
    private readonly CurrentWorkspace _current = new();

    public FolderSettingsTests()
    {
        _current.Open(Root);
        _repository.DirectoryExists(Root, Arg.Any<WorkspacePath>()).Returns(true);
        _store.ReadAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>()).Returns(FolderDefaults.Empty);
        _store.WriteAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<FolderDefaults>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);

    private void Given(string folder, FolderDefaults defaults) =>
        _store.ReadAsync(Root, P(folder), Arg.Any<CancellationToken>()).Returns(defaults);

    [Fact]
    public async Task Inheritance_ReadsEveryFolderFromRequestsDown()
    {
        Given("requests", new FolderDefaults([new KeyValueEntry("Accept", "*/*")], new BearerAuth("{{token}}")));
        Given("requests/users/admin", new FolderDefaults([new KeyValueEntry("X-Admin", "1")]));

        var result = await new FolderInheritance(_store).ForFolderAsync(Root, P("requests/users/admin"));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [new InheritedHeader("Accept", "*/*", P("requests")), new InheritedHeader("X-Admin", "1", P("requests/users/admin"))],
            result.Value.Headers);
        Assert.Equal(new BearerAuth("{{token}}"), result.Value.Auth);
        Received.InOrder(() =>
        {
            _store.ReadAsync(Root, P("requests"), Arg.Any<CancellationToken>());
            _store.ReadAsync(Root, P("requests/users"), Arg.Any<CancellationToken>());
            _store.ReadAsync(Root, P("requests/users/admin"), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Inheritance_AFolderOutsideRequestsInheritsNothing()
    {
        var result = await new FolderInheritance(_store).ForFolderAsync(Root, P("plans/perf"));

        Assert.Same(InheritedDefaults.None, result.Value);
        await _store.DidNotReceiveWithAnyArgs().ReadAsync(default!, default!, default);
    }

    [Fact]
    public async Task Inheritance_ReportsAnUnreadableFolderFile()
    {
        _store.ReadAsync(Root, P("requests/users"), Arg.Any<CancellationToken>())
            .Returns(new Error(ErrorCodes.InvalidFile, "bad"));

        var result = await new FolderInheritance(_store).ForFolderAsync(Root, P("requests/users/admin"));

        Assert.Equal(ErrorCodes.InvalidFile, result.Error?.Code);
    }

    [Fact]
    public async Task Load_ReturnsTheFolderSettingsWithInheritForUnsetAuth()
    {
        Given("requests/users", new FolderDefaults([new KeyValueEntry("Accept", "application/json", Enabled: false)]));

        var result = await new LoadFolderSettings(_repository, _store, _current).ExecuteAsync("requests/users");

        Assert.Equal([new KeyValueDto("Accept", "application/json", false)], result.Value.Headers);
        Assert.Equal("inherit", result.Value.Auth.Type);
    }

    [Theory]
    [InlineData("plans/perf", ErrorCodes.InvalidPath)]
    [InlineData("../outside", ErrorCodes.InvalidPath)]
    [InlineData("requests/.hidden", ErrorCodes.InvalidPath)]
    public async Task Load_RejectsFoldersOutsideRequests(string path, string code)
    {
        var result = await new LoadFolderSettings(_repository, _store, _current).ExecuteAsync(path);

        Assert.Equal(code, result.Error?.Code);
    }

    [Fact]
    public async Task Save_WritesTheSettingsWithoutEmptyRows()
    {
        var settings = new FolderSettingsDto(
            [new KeyValueDto(" Accept ", "application/json"), new KeyValueDto("", "")],
            new AuthDto("apiKey", Key: "X-Key", Value: "{{key}}"));

        var result = await new SaveFolderSettings(_repository, _store, _current).ExecuteAsync("requests", settings);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal([new KeyValueDto("Accept", "application/json")], result.Value.Headers);
        await _store.Received(1).WriteAsync(
            Root,
            P("requests"),
            Arg.Is<FolderDefaults>(d => d.Headers.SequenceEqual(new[] { new KeyValueEntry("Accept", "application/json") })
                && d.Auth.Equals(new ApiKeyAuth("X-Key", "{{key}}", ApiKeyLocation.Header))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_RejectsDuplicateHeaders()
    {
        var settings = new FolderSettingsDto([new KeyValueDto("Accept", "a"), new KeyValueDto("ACCEPT", "b")], new AuthDto("inherit"));

        var result = await new SaveFolderSettings(_repository, _store, _current).ExecuteAsync("requests/users", settings);

        Assert.Equal(ErrorCodes.InvalidRequest, result.Error?.Code);
        await _store.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Save_RejectsAMissingFolder()
    {
        _repository.DirectoryExists(Root, P("requests/gone")).Returns(false);

        var result = await new SaveFolderSettings(_repository, _store, _current)
            .ExecuteAsync("requests/gone", new FolderSettingsDto([], new AuthDto("none")));

        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
    }

    [Fact]
    public async Task Inherited_SaysWhereEachSettingComesFrom()
    {
        Given("requests", new FolderDefaults([new KeyValueEntry("Accept", "*/*")]));
        Given("requests/users", new FolderDefaults(auth: new BasicAuth("ada", "{{password}}")));

        var result = await new GetInheritedSettings(_repository, new FolderInheritance(_store), _current).ExecuteAsync("requests/users");

        Assert.Equal([new InheritedHeaderDto("Accept", "*/*", "requests")], result.Value.Headers);
        Assert.Equal(new AuthDto("basic", Username: "ada", Password: "{{password}}"), result.Value.Auth);
        Assert.Equal("requests/users", result.Value.AuthFolder);
    }

    [Fact]
    public async Task Inherited_WithoutFolderAuth_IsNoAuthFromNowhere()
    {
        var result = await new GetInheritedSettings(_repository, new FolderInheritance(_store), _current).ExecuteAsync("requests");

        Assert.Empty(result.Value.Headers);
        Assert.Equal("none", result.Value.Auth.Type);
        Assert.Null(result.Value.AuthFolder);
    }
}
