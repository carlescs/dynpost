using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Application.Interchange;
using Missive.Application.Workspaces;
using Missive.Desktop.Bridge;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Desktop.Tests.Bridge;

/// <summary>The workspace.import and workspace.export messages, through the real router and use cases.</summary>
public sealed class InterchangeBridgeTests : IDisposable
{
    private const string Root = "/work/my-api";

    private readonly IFileDialogs _dialogs = Substitute.For<IFileDialogs>();
    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IEnvironmentStore _environments = Substitute.For<IEnvironmentStore>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly IExternalFiles _files = Substitute.For<IExternalFiles>();
    private readonly ICollectionImporter _importer = Substitute.For<ICollectionImporter>();
    private readonly ICollectionExporter _exporter = Substitute.For<ICollectionExporter>();
    private readonly ServiceProvider _services;
    private readonly BridgeRouter _router;

    public InterchangeBridgeTests()
    {
        var current = new CurrentWorkspace();
        current.Open(Root);
        _exporter.Id.Returns("postman");
        _exporter.Name.Returns("Postman collection v2.1");
        _exporter.Extension.Returns(".postman_collection.json");
        _exporter.Export(Arg.Any<RequestCollection>()).Returns(new ExportOutput("{}", [], []));
        _files.WriteTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _files.ReadTextAsync("/in/shop.json", Arg.Any<CancellationToken>()).Returns(Result<string>.Success("{}"));
        _repository.ListAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<WorkspaceEntry>>.Success([]));
        _repository.CreateFolder(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.WriteRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _environments.ListAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<EnvironmentFile>>.Success([]));
        _secrets.LoadAsync(Root, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>.Success(
                new Dictionary<string, IReadOnlyDictionary<string, string>>()));

        _services = new ServiceCollection()
            .AddSingleton(current)
            .AddSingleton(_repository)
            .AddSingleton(_environments)
            .AddSingleton(_secrets)
            .AddSingleton(_files)
            .AddSingleton(_importer)
            .AddSingleton(_exporter)
            .AddTransient<ExportWorkspace>()
            .AddTransient<ImportCollection>()
            .BuildServiceProvider();
        _router = BridgeHandlers.Create(_services, _dialogs, Substitute.For<IAppWindow>());
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task ExportFormats_ListsTheExporters()
    {
        var response = await SendAsync("workspace.exportFormats", "null");

        var format = Assert.Single(response.GetProperty("payload").EnumerateArray().ToList());
        Assert.Equal("postman", format.GetProperty("id").GetString());
        Assert.Equal(".postman_collection.json", format.GetProperty("extension").GetString());
    }

    [Fact]
    public async Task Export_WritesWhereTheUserChose()
    {
        _dialogs.PickSaveFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<FileFilter>>()).Returns("/out/my-api.json");

        var response = await SendAsync("workspace.export", """{ "format": "postman", "includeEnvironments": false }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("/out/my-api.json", response.GetProperty("payload").GetProperty("path").GetString());
        await _files.Received(1).WriteTextAsync("/out/my-api.json", "{}", Arg.Any<CancellationToken>());
        await _environments.DidNotReceiveWithAnyArgs().ListAsync(default!, default);
    }

    [Fact]
    public async Task Export_CancelledReturnsNull()
    {
        _dialogs.PickSaveFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<FileFilter>>()).Returns((string?)null);

        var response = await SendAsync("workspace.export", """{ "format": "postman" }""");

        Assert.True(response.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("payload").ValueKind);
        await _files.DidNotReceiveWithAnyArgs().WriteTextAsync(default!, default!, default);
    }

    [Fact]
    public async Task Export_UnknownFormatFailsBeforeAskingForAFile()
    {
        var response = await SendAsync("workspace.export", """{ "format": "word" }""");

        Assert.False(response.GetProperty("ok").GetBoolean());
        await _dialogs.DidNotReceiveWithAnyArgs().PickSaveFileAsync(default!, default!);
    }

    [Fact]
    public async Task Import_ImportsTheChosenFile()
    {
        _dialogs.PickFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<FileFilter>>()).Returns("/in/shop.json");
        _importer.Parse("{}", "shop.json").Returns(new ParsedCollection(
            "Postman collection v2.1",
            new RequestCollection(new CollectionFolder("Shop", [], [new HttpRequestDefinition("Ping", RequestMethod.Get, "/ping")]), []),
            []));

        var response = await SendAsync("workspace.import", """{ "folder": "requests" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var payload = response.GetProperty("payload");
        Assert.Equal("Postman collection v2.1", payload.GetProperty("format").GetString());
        Assert.Equal("requests/Shop", payload.GetProperty("folder").GetString());
        Assert.Equal(1, payload.GetProperty("requests").GetInt32());
    }

    [Fact]
    public async Task Import_CancelledReturnsNull()
    {
        _dialogs.PickFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<FileFilter>>()).Returns((string?)null);

        var response = await SendAsync("workspace.import", """{ "folder": "requests" }""");

        Assert.Equal(JsonValueKind.Null, response.GetProperty("payload").ValueKind);
        _importer.DidNotReceiveWithAnyArgs().Parse(default!, default!);
    }

    private async Task<JsonElement> SendAsync(string type, string payload)
    {
        var json = await _router.HandleAsync($$"""{ "id": "1", "type": "{{type}}", "payload": {{payload}} }""");
        Assert.NotNull(json);
        return JsonDocument.Parse(json).RootElement;
    }
}
