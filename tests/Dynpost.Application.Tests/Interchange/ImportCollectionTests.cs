using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Folders;
using Dynpost.Application.Interchange;
using Dynpost.Application.Runs;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Environments;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Runs;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests.Interchange;

public class ImportCollectionTests
{
    private const string Root = "/work/my-api";
    private const string Source = "/downloads/shop.json";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IFolderDefaultsStore _folderDefaults = Substitute.For<IFolderDefaultsStore>();
    private readonly IRunPlanStore _plans = Substitute.For<IRunPlanStore>();
    private readonly IRunPlanFormat _planFormat = Substitute.For<IRunPlanFormat>();
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
        _plans.WriteAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        _planFormat.Write(Arg.Any<RunPlan>()).Returns(call => $"plan {call.Arg<RunPlan>().Name}\n");
        _files.WriteTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _folderDefaults.WriteAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<FolderDefaults>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _environments.WriteAsync(Root, Arg.Any<string>(), Arg.Any<EnvironmentDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _secrets.LoadAsync(Root, Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>.Success(
                new Dictionary<string, IReadOnlyDictionary<string, string>>()));
        _secrets.SaveAsync(Root, Arg.Any<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(), Arg.Any<CancellationToken>())
            .Returns(Unit.Value);
    }

    private ImportCollection UseCase() => new(_repository, _folderDefaults, _plans, _planFormat, _environments, _secrets, _files, _importer, _current);

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
    public async Task CleansUpEntriesDynpostCannotStore()
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

        var noWorkspace = new ImportCollection(_repository, _folderDefaults, _plans, _planFormat, _environments, _secrets, _files, _importer, new CurrentWorkspace());
        Assert.Equal(ErrorCodes.NoWorkspace, (await noWorkspace.ExecuteAsync(new ImportCollectionRequest(Source))).Error?.Code);
    }

    [Fact]
    public async Task WritesTheSettingsOfImportedFolders()
    {
        var defaults = new FolderDefaults(
            headers: [new KeyValueEntry("Accept", "application/json"), new KeyValueEntry("accept", "text/plain")],
            auth: new BasicAuth("{{user}}", "{{password}}"),
            variables: [new KeyValueEntry("baseUrl", "https://x.test"), new KeyValueEntry("bad name", "1")]);
        Returns(new CollectionFolder(
            "Plan",
            [new CollectionFolder("Users", [], [new HttpRequestDefinition("Get user", RequestMethod.Get, "/users/1")])],
            [new HttpRequestDefinition("Health", RequestMethod.Get, "/health")])
        {
            Defaults = defaults,
        });

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _folderDefaults.Received(1).WriteAsync(
            Root,
            P("requests/Plan"),
            Arg.Is<FolderDefaults>(d =>
                d.Headers.Count == 1 && d.Headers[0].Value == "application/json"
                && d.Auth == new BasicAuth("{{user}}", "{{password}}")
                && d.Variables.Count == 1 && d.Variables[0].Key == "baseUrl"),
            Arg.Any<CancellationToken>());

        // Folders without settings get no _folder.yaml.
        await _folderDefaults.DidNotReceive().WriteAsync(Root, P("requests/Plan/Users"), Arg.Any<FolderDefaults>(), Arg.Any<CancellationToken>());
        Assert.Contains(result.Value.Warnings, w => w.Contains("repeated header 'accept'", StringComparison.Ordinal));
        Assert.Contains(result.Value.Warnings, w => w.Contains("'bad name'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SkipsFolderSettingsThatCantBeSaved()
    {
        Returns(new CollectionFolder("Plan", [], [new HttpRequestDefinition("Health", RequestMethod.Get, "/health")])
        {
            Defaults = FolderDefaults.Empty with { Settings = new RequestSettings(MaxRedirects: -1) },
        });

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _folderDefaults.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default);
        Assert.Contains(result.Value.Warnings, w => w.StartsWith("Skipped the settings of the folder 'Plan'", StringComparison.Ordinal));
        Assert.Equal(1, result.Value.Requests);
    }

    private static readonly RunLoad Load = new(5, 10, null, TimeSpan.FromSeconds(2), TimeSpan.Zero, TimeSpan.Zero, null);

    private void ReturnsPlans(CollectionFolder root, params ImportedRunPlan[] plans) =>
        _importer.Parse("{}", "shop.json").Returns(new ParsedCollection("Test format", new RequestCollection(root, []) { Plans = plans }, []));

    private RunPlan WrittenPlan()
    {
        var call = _planFormat.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IRunPlanFormat.Write));
        return (RunPlan)call.GetArguments()[0]!;
    }

    [Fact]
    public async Task WritesRunPlansWithThePathsTheirRequestsWereSavedAt()
    {
        var list = new HttpRequestDefinition("List", RequestMethod.Get, "/list");
        var buy = new HttpRequestDefinition("Buy", RequestMethod.Post, "/buy");
        var off = new HttpRequestDefinition("Off", RequestMethod.Get, "/off");
        var ok = new RunAssertion(["2xx"], null, null, null);
        ReturnsPlans(
            new CollectionFolder("Shop", [new CollectionFolder("Cart", [], [buy, off])], [list]),
            new ImportedRunPlan(
                "Browse load",
                [new ImportedPlanRequest(list, Weight: 3), new ImportedPlanRequest(buy, Assertion: ok with { Status = ["201"] }), new ImportedPlanRequest(off, Enabled: false)],
                Load,
                RequestOrder.Random,
                ok));

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(["plans/Shop/browse-load.run.yaml"], result.Value.Plans);
        await _plans.Received(1).WriteAsync(Root, P("plans/Shop/browse-load.run.yaml"), "plan Browse load\n", Arg.Any<CancellationToken>());

        var plan = WrittenPlan();
        Assert.Equal(["requests/Shop/list.yaml", "requests/Shop/Cart/buy.yaml", "requests/Shop/Cart/off.yaml"], plan.Requests.Select(r => r.Value));
        Assert.Equal(["requests/Shop/Cart/off.yaml"], plan.DisabledRequests);
        Assert.Equal(RequestOrder.Random, plan.Order);
        Assert.Equal(3, plan.RequestWeights["requests/Shop/list.yaml"]);
        Assert.Single(plan.RequestWeights);
        Assert.Equal(ok, plan.Assertions[RunPlan.AllRequests]);
        Assert.Equal(["201"], plan.Assertions["requests/Shop/Cart/buy.yaml"].Status);
        Assert.Equal(Load, plan.Load);
        Assert.Null(plan.Data);
    }

    [Fact]
    public async Task CopiesThePlansCsvFileIntoTheWorkspaceAsCommaSeparatedValues()
    {
        var list = new HttpRequestDefinition("List", RequestMethod.Get, "/list");
        ReturnsPlans(
            new CollectionFolder("Shop", [], [list]),
            new ImportedRunPlan(
                "Load",
                [new ImportedPlanRequest(list)],
                Load,
                Data: new ImportedDataFile("data/users.csv", SelectionMode.PerThread, EndBehavior.Stop, "user,pass", SkipFirstLine: true, Delimiter: ";")));
        _files.ReadTextAsync("/downloads/data/users.csv", Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("name;pw\r\nann;1\r\nbo;x,y\r\n"));
        _repository.EntryExists(Root, P("data/Shop")).Returns(true);

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _files.Received(1).WriteTextAsync(
            Path.Combine(Root, "data/Shop 2/users.csv"),
            "user,pass\nann,1\nbo,\"x,y\"\n",
            Arg.Any<CancellationToken>());
        Assert.Equal(new RunDataSource(P("data/Shop 2/users.csv"), SelectionMode.PerThread, EndBehavior.Stop), WrittenPlan().Data);
    }

    [Fact]
    public async Task WritesPlansWithoutTheirDataFileWhenItCantBeRead()
    {
        var list = new HttpRequestDefinition("List", RequestMethod.Get, "/list");
        ReturnsPlans(
            new CollectionFolder("Shop", [], [list]),
            new ImportedRunPlan("Load", [new ImportedPlanRequest(list)], Load, Data: new ImportedDataFile("/elsewhere/missing.csv", SelectionMode.Sequential, EndBehavior.Recycle)));
        _files.ReadTextAsync("/elsewhere/missing.csv", Arg.Any<CancellationToken>()).Returns(new Error(ErrorCodes.NotFound, "The file '/elsewhere/missing.csv' does not exist."));

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Single(result.Value.Plans);
        Assert.Null(WrittenPlan().Data);
        Assert.Contains(result.Value.Warnings, w => w.Contains("has no data file", StringComparison.Ordinal) && w.Contains("missing.csv", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SkipsPlansThatCantBeSaved()
    {
        var list = new HttpRequestDefinition("List", RequestMethod.Get, "/list");
        var notImported = new HttpRequestDefinition("Gone", RequestMethod.Get, "/gone");
        ReturnsPlans(
            new CollectionFolder("Shop", [], [list]),
            new ImportedRunPlan("Orphan", [new ImportedPlanRequest(notImported)], Load),
            new ImportedRunPlan("Off", [new ImportedPlanRequest(list, Enabled: false)], Load),
            new ImportedRunPlan("Partly", [new ImportedPlanRequest(list), new ImportedPlanRequest(notImported)], Load));

        var result = await UseCase().ExecuteAsync(new ImportCollectionRequest(Source));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(["plans/Shop/partly.run.yaml"], result.Value.Plans);
        Assert.Contains("Skipped the run plan 'Orphan': none of its requests was imported.", result.Value.Warnings);
        Assert.Contains(result.Value.Warnings, w => w.StartsWith("Skipped the run plan 'Off': Enable at least one request", StringComparison.Ordinal));
        Assert.Contains("The run plan 'Partly' leaves out 1 request(s) that were not imported.", result.Value.Warnings);
    }
}
