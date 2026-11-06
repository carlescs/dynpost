using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Application.Interchange;
using Missive.Application.Workspaces;
using Missive.Domain.Environments;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests.Interchange;

public class ExportWorkspaceTests
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IEnvironmentStore _environments = Substitute.For<IEnvironmentStore>();
    private readonly IExternalFiles _files = Substitute.For<IExternalFiles>();
    private readonly ICollectionExporter _exporter = Substitute.For<ICollectionExporter>();
    private readonly CurrentWorkspace _current = new();
    private RequestCollection? _exported;

    public ExportWorkspaceTests()
    {
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

    private ExportWorkspace UseCase() => new(_repository, _environments, _files, [_exporter], _current);

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
}
