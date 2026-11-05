using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Application.Workspaces;
using Missive.Domain.Environments;
using NSubstitute;

namespace Missive.Application.Tests;

using Secrets = IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>;

public class EnvironmentUseCaseTests
{
    private const string Root = "/work/my-api";

    private readonly IEnvironmentStore _store = Substitute.For<IEnvironmentStore>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly IWorkspaceStateStore _state = Substitute.For<IWorkspaceStateStore>();
    private readonly CurrentWorkspace _current = new();

    public EnvironmentUseCaseTests()
    {
        _current.Open(Root);
        _state.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(WorkspaceState.Default);
        _state.SaveAsync(Root, Arg.Any<WorkspaceState>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        StoredSecrets(new Dictionary<string, IReadOnlyDictionary<string, string>>());
        _secrets.SaveAsync(Root, Arg.Any<Secrets>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _store.WriteAsync(Root, Arg.Any<string>(), Arg.Any<EnvironmentDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _store.Move(Root, Arg.Any<string>(), Arg.Any<string>()).Returns(Unit.Value);
        _store.Delete(Root, Arg.Any<string>()).Returns(Unit.Value);
    }

    private void StoredSecrets(Dictionary<string, IReadOnlyDictionary<string, string>> secrets) =>
        _secrets.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<Secrets>.Success(secrets));

    private void Existing(string id, EnvironmentDefinition environment)
    {
        _store.Exists(Root, id).Returns(true);
        _store.ReadAsync(Root, id, Arg.Any<CancellationToken>()).Returns(environment);
    }

    private static EnvironmentDto Dto(string name, params EnvironmentVariableDto[] variables) => new(name, variables);

    [Fact]
    public async Task List_SortsByNameAndReportsUnreadableFiles()
    {
        _store.ListAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<EnvironmentFile>>.Success([
            new EnvironmentFile("broken.yaml", new Error(ErrorCodes.InvalidFile, "bad")),
            new EnvironmentFile("prod.yaml", new EnvironmentDefinition("Production")),
            new EnvironmentFile("dev.yaml", new EnvironmentDefinition("development")),
        ]));
        _state.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(new WorkspaceState("prod.yaml"));

        var result = await new ListEnvironments(_store, _state, _current).ExecuteAsync();

        Assert.Equal("prod.yaml", result.Value.Active);
        Assert.Equal(
            [
                new EnvironmentSummaryDto("broken.yaml", "broken.yaml", "bad"),
                new EnvironmentSummaryDto("dev.yaml", "development"),
                new EnvironmentSummaryDto("prod.yaml", "Production"),
            ],
            result.Value.Environments);
    }

    [Fact]
    public async Task List_ForgetsASelectionWhoseFileIsGone()
    {
        _store.ListAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<EnvironmentFile>>.Success([]));
        _state.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(new WorkspaceState("gone.yaml"));

        var result = await new ListEnvironments(_store, _state, _current).ExecuteAsync();

        Assert.Null(result.Value.Active);
    }

    [Fact]
    public async Task List_RequiresAWorkspace()
    {
        var result = await new ListEnvironments(_store, _state, new CurrentWorkspace()).ExecuteAsync();

        Assert.Equal(ErrorCodes.NoWorkspace, result.Error?.Code);
    }

    [Fact]
    public async Task Load_FillsInSecretValues()
    {
        Existing("dev.yaml", new EnvironmentDefinition("Dev", [
            new EnvironmentVariable("baseUrl", "http://localhost"),
            new EnvironmentVariable("token", "", Secret: true),
            new EnvironmentVariable("password", "", Secret: true),
        ]));
        StoredSecrets(new() { ["dev.yaml"] = new Dictionary<string, string> { ["token"] = "abc" } });

        var result = await new LoadEnvironment(_store, _secrets, _current).ExecuteAsync("dev.yaml");

        Assert.Equal(
            [
                new EnvironmentVariableDto("baseUrl", "http://localhost"),
                new EnvironmentVariableDto("token", "abc", true),
                new EnvironmentVariableDto("password", "", true),
            ],
            result.Value.Variables);
    }

    [Theory]
    [InlineData("../secrets.yaml")]
    [InlineData("sub/dev.yaml")]
    [InlineData("dev.txt")]
    [InlineData(".hidden.yaml")]
    public async Task Load_RejectsInvalidIds(string id)
    {
        var result = await new LoadEnvironment(_store, _secrets, _current).ExecuteAsync(id);

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
        await _store.DidNotReceiveWithAnyArgs().ReadAsync(default!, default!, default);
    }

    [Fact]
    public async Task Create_WritesASlugNamedFileAndStoresSecretsSeparately()
    {
        _store.Exists(Root, "staging.yaml").Returns(true);

        var result = await new CreateEnvironment(_store, _secrets, _current).ExecuteAsync(Dto(
            " Staging ",
            new EnvironmentVariableDto("token", "s3cret", true),
            new EnvironmentVariableDto("baseUrl", "https://staging"),
            new EnvironmentVariableDto("", "")));

        Assert.Equal("staging-2.yaml", result.Value.Id);
        Assert.Equal("Staging", result.Value.Environment.Name);
        // Plain variables come first, as in the file.
        Assert.Equal(["baseUrl", "token"], result.Value.Environment.Variables.Select(v => v.Name));
        await _store.Received(1).WriteAsync(
            Root,
            "staging-2.yaml",
            Arg.Is<EnvironmentDefinition>(e => e.Variables.Count == 2 && e.Variables[1].Secret),
            Arg.Any<CancellationToken>());
        await _secrets.Received(1).SaveAsync(
            Root,
            Arg.Is<Secrets>(s => s.Count == 1 && s["staging-2.yaml"]["token"] == "s3cret"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_RejectsInvalidEnvironments()
    {
        var result = await new CreateEnvironment(_store, _secrets, _current).ExecuteAsync(Dto(
            "Dev",
            new EnvironmentVariableDto("a", "{{b}}"),
            new EnvironmentVariableDto("b", "{{a}}")));

        Assert.Equal(ErrorCodes.InvalidRequest, result.Error?.Code);
        await _store.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Create_WithoutSecrets_DoesNotTouchTheSecretStore()
    {
        var result = await new CreateEnvironment(_store, _secrets, _current).ExecuteAsync(Dto("Dev"));

        Assert.True(result.IsSuccess);
        await _secrets.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default);
    }

    [Fact]
    public async Task Save_RenamesTheFileAndMovesSecretsAndSelection()
    {
        Existing("dev.yaml", new EnvironmentDefinition("Dev"));
        _state.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(new WorkspaceState("dev.yaml"));
        StoredSecrets(new()
        {
            ["dev.yaml"] = new Dictionary<string, string> { ["token"] = "old" },
            ["prod.yaml"] = new Dictionary<string, string> { ["token"] = "p" },
        });

        var result = await new SaveEnvironment(_store, _secrets, _state, _current).ExecuteAsync(
            "dev.yaml",
            Dto("Local", new EnvironmentVariableDto("token", "new", true)));

        Assert.Equal("local.yaml", result.Value.Id);
        _store.Received(1).Move(Root, "dev.yaml", "local.yaml");
        await _state.Received(1).SaveAsync(Root, new WorkspaceState("local.yaml"), Arg.Any<CancellationToken>());
        await _secrets.Received(1).SaveAsync(
            Root,
            Arg.Is<Secrets>(s => s.Count == 2 && s["local.yaml"]["token"] == "new" && s["prod.yaml"]["token"] == "p"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_KeepsHandPickedFileNames()
    {
        Existing("my-env.yaml", new EnvironmentDefinition("Dev"));

        var result = await new SaveEnvironment(_store, _secrets, _state, _current).ExecuteAsync("my-env.yaml", Dto("Local"));

        Assert.Equal("my-env.yaml", result.Value.Id);
        _store.DidNotReceiveWithAnyArgs().Move(default!, default!, default!);
    }

    [Fact]
    public async Task Save_UnchangedSecrets_AreNotRewritten()
    {
        Existing("dev.yaml", new EnvironmentDefinition("Dev"));
        StoredSecrets(new() { ["dev.yaml"] = new Dictionary<string, string> { ["token"] = "abc" } });

        await new SaveEnvironment(_store, _secrets, _state, _current).ExecuteAsync(
            "dev.yaml",
            Dto("Dev", new EnvironmentVariableDto("token", "abc", true)));

        await _secrets.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default);
    }

    [Fact]
    public async Task Save_UnreadableSecrets_AreNotOverwritten()
    {
        _secrets.LoadAsync(Root, Arg.Any<CancellationToken>())
            .Returns(Result<Secrets>.Failure(ErrorCodes.InvalidFile, "bad secrets"));

        var result = await new SaveEnvironment(_store, _secrets, _state, _current).ExecuteAsync("dev.yaml", Dto("Dev"));

        Assert.Equal(ErrorCodes.InvalidFile, result.Error?.Code);
        await _store.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default);
        await _secrets.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default);
    }

    [Fact]
    public async Task Delete_RemovesTheFileItsSecretsAndTheSelection()
    {
        Existing("dev.yaml", new EnvironmentDefinition("Dev"));
        _state.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(new WorkspaceState("dev.yaml"));
        StoredSecrets(new() { ["dev.yaml"] = new Dictionary<string, string> { ["token"] = "abc" } });

        var result = await new DeleteEnvironment(_store, _secrets, _state, _current).ExecuteAsync("dev.yaml");

        Assert.True(result.IsSuccess);
        _store.Received(1).Delete(Root, "dev.yaml");
        await _state.Received(1).SaveAsync(Root, WorkspaceState.Default, Arg.Any<CancellationToken>());
        await _secrets.Received(1).SaveAsync(Root, Arg.Is<Secrets>(s => s.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_MissingFile_IsNotFound()
    {
        var result = await new DeleteEnvironment(_store, _secrets, _state, _current).ExecuteAsync("dev.yaml");

        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
    }

    [Fact]
    public async Task Select_StoresTheSelection()
    {
        _store.Exists(Root, "dev.yaml").Returns(true);

        var result = await new SelectEnvironment(_store, _state, _current).ExecuteAsync("dev.yaml");

        Assert.True(result.IsSuccess);
        await _state.Received(1).SaveAsync(Root, new WorkspaceState("dev.yaml"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Select_RefusesMissingEnvironments()
    {
        var result = await new SelectEnvironment(_store, _state, _current).ExecuteAsync("gone.yaml");

        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
        await _state.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default);
    }

    [Fact]
    public async Task Select_None_WhenNothingIsSelected_WritesNothing()
    {
        var result = await new SelectEnvironment(_store, _state, _current).ExecuteAsync(null);

        Assert.True(result.IsSuccess);
        await _state.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default);
    }
}
