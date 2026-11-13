using Dynypost.Application.Common;
using Dynypost.Application.Environments;
using Dynypost.Application.Interchange;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Environments;
using Dynypost.Domain.Requests;
using Dynypost.Domain.Workspaces;
using NSubstitute;

namespace Dynypost.Application.Tests.Interchange;

public class ImportCollectionTests
{
    private const string Root = "/work/my-api";
    private const string Source = "/downloads/shop.json";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IEnvironmentStore _environments = Substitute.For<IEnvironmentStore>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly IExternalFiles _files = Substitute.For<IExternalFiles>();
    private readonly ICollectionImporter _importer = Substitute.For<ICollectionImporter>();
    private readonly CurrentWorkspace _current = new();

    public ImportCollectionTests()
    {
        _current.Open(Root);
        _files.ReadTextAsync(Source, Arg.Any<CancellationToken>()).Returns(Result<string>.Success("{}"));
        _repository.CreateFolder(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.WriteRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _environments.WriteAsync(Root, Arg.Any<string>(), Arg.Any<EnvironmentDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _secrets.LoadAsync(Root, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>.Success(
                new Dictionary<string, IReadOnlyDictionary<string, string>>()));
        _secrets.SaveAsync(Root, Arg.Any<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(), Arg.Any<CancellationToken>())
            .Returns(Unit.Value);
    }

    private ImportCollection UseCase() => new(_repository, _environments, _secrets, _files, _importer, _current);

    private void Returns(CollectionFolder root, params EnvironmentDefinition[] environments) =>
        _importer.Parse("{}", "shop.json").Returns(new ParsedCollection("Test format", new RequestCollection(root, environments), ["note"]));

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);

    [Fact]
    public async Task WritesRequestsIntoANewFolderNamedAfterTheCollection()
    {
        Returns(new CollectionFolder(
            "Shop: API",
            [new CollectionFolder("Users", [], [new HttpRequestDefinition("Get user", RequestMethod.Get, "/users/1")])],
            [new HttpRequestDefinition("Health", RequestMethod.Get, "/health")]));
        _repository.EntryExists(Root, P("requests/Shop- API")).Returns(true);

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("requests/Shop- API 2", result.Value.Folder);
        Assert.Equal(2, result.Value.Requests);
        Assert.Equal(2, result.Value.Folders);
        Assert.Equal(["note"], result.Value.Warnings);
        _repository.Received(1).CreateFolder(Root, P("requests/Shop- API 2"));
        _repository.Received(1).CreateFolder(Root, P("requests/Shop- API 2/Users"));
        await _repository.Received(1).WriteRequestAsync(Root, P("requests/Shop- API 2/health.yaml"), Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).WriteRequestAsync(Root, P("requests/Shop- API 2/Users/get-user.yaml"), Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NumbersRequestFilesWhoseNamesAreTaken()
    {
        Returns(new CollectionFolder("Shop", [], [
            new HttpRequestDefinition("Ping", RequestMethod.Get, "/a"),
            new HttpRequestDefinition("Ping", RequestMethod.Get, "/b"),
        ]));
        _repository.WhenForAnyArgs(r => r.WriteRequestAsync(default!, default!, default!, default))
            .Do(call => _repository.FileExists(Root, call.ArgAt<WorkspacePath>(1)).Returns(true));

        await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        await _repository.Received(1).WriteRequestAsync(Root, P("requests/Shop/ping.yaml"), Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).WriteRequestAsync(Root, P("requests/Shop/ping-2.yaml"), Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleansUpEntriesDynypostCannotStore()
    {
        Returns(new CollectionFolder("Shop", [], [
            new HttpRequestDefinition(
                "  ",
                RequestMethod.Post,
                "/items",
                [new KeyValueEntry("id", "1"), new KeyValueEntry("id", "2"), new KeyValueEntry("~flag", "x"), new KeyValueEntry("", "")],
                [new KeyValueEntry("Accept", "a"), new KeyValueEntry("accept", "b")]),
        ]));

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        await _repository.Received(1).WriteRequestAsync(
            Root,
            P("requests/Shop/post-items.yaml"),
            Arg.Is<HttpRequestDefinition>(r =>
                r.Name == "POST /items"
                && r.Params.Select(p => p.Key).SequenceEqual(new[] { "id", "flag" })
                && r.Headers.Count == 1),
            Arg.Any<CancellationToken>());
        Assert.Contains(result.Value.Warnings, w => w.Contains("repeated query parameter 'id'"));
        Assert.Contains(result.Value.Warnings, w => w.Contains("repeated header 'accept'"));
    }

    [Fact]
    public async Task CreatesEnvironmentsAndStoresTheirSecretsLocally()
    {
        Returns(
            new CollectionFolder("Shop", [], []),
            new EnvironmentDefinition("Staging", [
                new EnvironmentVariable("token", "s3cr3t", Secret: true),
                new EnvironmentVariable("baseUrl", "https://staging.test"),
                new EnvironmentVariable("bad name", "x"),
            ]));
        _environments.Exists(Root, "staging.yaml").Returns(true);

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        Assert.Null(result.Value.Folder);
        Assert.Equal(["staging-2.yaml"], result.Value.Environments);
        await _environments.Received(1).WriteAsync(
            Root,
            "staging-2.yaml",
            Arg.Is<EnvironmentDefinition>(e => e.Name == "Staging"
                && e.Variables.Select(v => v.Name).SequenceEqual(new[] { "baseUrl", "token" })),
            Arg.Any<CancellationToken>());
        await _secrets.Received(1).SaveAsync(
            Root,
            Arg.Is<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(s => s["staging-2.yaml"]["token"] == "s3cr3t"),
            Arg.Any<CancellationToken>());
        Assert.Contains(result.Value.Warnings, w => w.Contains("bad name"));
        _repository.DidNotReceiveWithAnyArgs().CreateFolder(default!, default!);
    }

    [Fact]
    public async Task ImportsIntoTheChosenFolder()
    {
        Returns(new CollectionFolder("environments", [], [new HttpRequestDefinition("A", RequestMethod.Get, "/a")]));
        _repository.DirectoryExists(Root, P("requests/team")).Returns(true);

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source, "requests/team"));

        Assert.Equal("requests/team/environments", result.Value.Folder);
    }

    [Fact]
    public async Task ImportsIntoTheRequestsFolderByDefault()
    {
        Returns(new CollectionFolder("environments", [], [new HttpRequestDefinition("A", RequestMethod.Get, "/a")]));

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        // "environments" isn't reserved inside requests/.
        Assert.Equal("requests/environments", result.Value.Folder);
    }

    [Fact]
    public async Task RefusesFilesWithNothingToImport()
    {
        Returns(new CollectionFolder("Empty", [], []));

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        Assert.Equal(ErrorCodes.InvalidFile, result.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().CreateFolder(default!, default!);
    }

    [Fact]
    public async Task PassesOnParseErrors()
    {
        _importer.Parse("{}", "shop.json").Returns(new Error(ErrorCodes.InvalidFile, "Not a collection."));

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        Assert.Equal("Not a collection.", result.Error?.Message);
    }

    [Fact]
    public async Task NeedsAWorkspaceAndAnExistingFolder()
    {
        Assert.Equal(ErrorCodes.NotFound, (await UseCase().ExecuteAsync(new ImportCollectionRequest(Source, "requests/missing"))).Error?.Code);

        var noWorkspace = new ImportCollection(_repository, _environments, _secrets, _files, _importer, new CurrentWorkspace());
        Assert.Equal(ErrorCodes.NoWorkspace, (await noWorkspace.ExecuteAsync(new ImportCollectionRequest(Source))).Error?.Code);
    }
}
