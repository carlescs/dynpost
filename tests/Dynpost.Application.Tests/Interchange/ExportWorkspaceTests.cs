using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Folders;
using Dynpost.Application.Interchange;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Environments;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests.Interchange;

public class ExportWorkspaceTests
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IEnvironmentStore _environments = Substitute.For<IEnvironmentStore>();
    private readonly IExternalFiles _files = Substitute.For<IExternalFiles>();
    private readonly ICollectionExporter _exporter = Substitute.For<ICollectionExporter>();
    private readonly CurrentWorkspace _current = new();
    private readonly IFolderDefaultsStore _folders = Substitute.For<IFolderDefaultsStore>();
    private RequestCollection? _exported;

    public ExportWorkspaceTests()
    {
        _folders.ReadAsync(Arg.Any<string>(), Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>()).Returns(FolderDefaults.Empty);
        _current.Open(Root);
        _exporter.Id.Returns("test");
        _exporter.Name.Returns("Test format");
        _exporter.Extension.Returns(".test.json");
        _exporter.Export(Arg.Do<RequestCollection>(c => _exported = c))
            .Returns(new ExportOutput("main", [new ExportedFile("dev.env.json", "env")], ["exporter note"]));
        _files.WriteTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);

        var get = new HttpRequestDefinition("Get user", RequestMethod.Get, "{{baseUrl}}/users/1");
        _repository.ListAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<WorkspaceEntry>>.Success([
            new FolderEntry(P("users"), [
                new RequestFileEntry(P("users/get-user.yaml"), get),
                new RequestFileEntry(P("users/broken.yaml"), new Error(ErrorCodes.InvalidFile, "bad yaml")),
            ]),
            new RequestFileEntry(P("health.yaml"), new HttpRequestDefinition("Health", RequestMethod.Get, "/health")),
        ]));
        _environments.ListAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<EnvironmentFile>>.Success([
            new EnvironmentFile("dev.yaml", new EnvironmentDefinition("Dev", [
                new EnvironmentVariable("baseUrl", "http://localhost"),
                new EnvironmentVariable("token", "leaked?", Secret: true),
            ])),
            new EnvironmentFile("bad.yaml", new Error(ErrorCodes.InvalidFile, "broken")),
        ]));
    }

    private ExportWorkspace UseCase() => new(_repository, _environments, _files, [_exporter], _current, new FolderInheritance(_folders));

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);

    [Fact]
    public async Task ExportsTheWorkspaceTreeAndEnvironmentsWithoutSecretValues()
    {
        var result = await UseCase().ExecuteAsync(new ExportWorkspaceRequest("test", "/out/api.test.json"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("my-api", _exported!.Root.Name);
        Assert.Equal("Health", Assert.Single(_exported.Root.Requests).Name);
        Assert.Equal("Get user", Assert.Single(Assert.Single(_exported.Root.Folders).Requests).Name);
        var dev = Assert.Single(_exported.Environments);
        Assert.Equal(string.Empty, dev.Variables.Single(v => v.Secret).Value);

        Assert.Equal(2, result.Value.Requests);
        Assert.Equal(1, result.Value.Environments);
        Assert.Contains(result.Value.Warnings, w => w.Contains("users/broken.yaml"));
        Assert.Contains(result.Value.Warnings, w => w.Contains("bad.yaml"));
        Assert.Contains("exporter note", result.Value.Warnings);
        await _files.Received(1).WriteTextAsync("/out/api.test.json", "main", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaysFolderVariablesAreNotExported()
    {
        _folders.ReadAsync(Root, WorkspacePath.Parse("requests"), Arg.Any<CancellationToken>())
            .Returns(new FolderDefaults(variables: [new KeyValueEntry("page", "1")]));

        var result = await UseCase().ExecuteAsync(new ExportWorkspaceRequest("test", "/out/api.test.json"));

        Assert.Contains("1 folder sets variables; folder variables are not exported, so its requests keep their {{references}}.", result.Value.Warnings);
        Assert.Equal("{{baseUrl}}/users/1", Assert.Single(Assert.Single(_exported!.Root.Folders).Requests).Url);
    }

    [Fact]
    public async Task WritesCompanionFilesUnderFreeNames()
    {
        // Companions go in the destination's folder, in the platform's path form.
        var folder = Path.GetDirectoryName("/out/api.test.json")!;
        _files.Exists(Path.Combine(folder, "dev.env.json")).Returns(true);

        var result = await UseCase().ExecuteAsync(new ExportWorkspaceRequest("test", "/out/api.test.json"));

        var companion = Path.Combine(folder, "dev-2.env.json");
        Assert.Equal(["/out/api.test.json", companion], result.Value.Files);
        await _files.Received(1).WriteTextAsync(companion, "env", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddsTheFormatsExtensionWhenTheFileHasNone()
    {
        var result = await UseCase().ExecuteAsync(new ExportWorkspaceRequest("TEST", "/out/api", IncludeEnvironments: false));

        Assert.Equal("/out/api.test.json", result.Value.Path);
        Assert.Empty(_exported!.Environments);
        await _environments.DidNotReceiveWithAnyArgs().ListAsync(default!, default);
    }

    [Fact]
    public async Task RefusesUnknownFormats()
    {
        var result = await UseCase().ExecuteAsync(new ExportWorkspaceRequest("word", "/out/api.docx"));

        Assert.Equal(ErrorCodes.InvalidRequest, result.Error?.Code);
        await _files.DidNotReceiveWithAnyArgs().WriteTextAsync(default!, default!, default);
    }

    [Fact]
    public void ListsTheFormats()
    {
        Assert.Equal([new ExportFormatDto("test", "Test format", ".test.json")], UseCase().Formats());
    }

    [Fact]
    public async Task FormatsThatRecordExchanges_GetEachRequestResolvedWithItsLatestResponse()
    {
        _exporter.NeedsExchanges.Returns(true);
        var secrets = Substitute.For<ISecretStore>();
        var captures = Substitute.For<ICapturedValueStore>();
        var state = Substitute.For<IWorkspaceStateStore>();
        var history = Substitute.For<Dynpost.Application.History.IHistoryStore>();
        state.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(new WorkspaceState("dev.yaml"));
        _environments.Exists(Root, "dev.yaml").Returns(true);
        _environments.ReadAsync(Root, "dev.yaml", Arg.Any<CancellationToken>()).Returns(new EnvironmentDefinition("Dev", [new EnvironmentVariable("baseUrl", "https://api.test")]));
        var none = Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>.Success(new Dictionary<string, IReadOnlyDictionary<string, string>>());
        secrets.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(none);
        captures.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(none);
        history.ListAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Dynpost.Application.History.HistoryEntryDto>>.Success([]));
        var useCase = new ExportWorkspace(
            _repository, _environments, _files, [_exporter], _current, new FolderInheritance(_folders),
            new ActiveEnvironment(_environments, secrets, captures, state, _current), history);

        var result = await useCase.ExecuteAsync(new ExportWorkspaceRequest("test", "/out/api.har"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var get = Assert.Single(_exported!.Exchanges, e => e.Name == "Get user");
        Assert.Equal("https://api.test/users/1", get.Request!.Url);
        Assert.Null(get.Latest);
        var health = Assert.Single(_exported.Exchanges, e => e.Name == "Health");
        Assert.Null(health.Request);
        Assert.NotNull(health.Error);
    }
}
