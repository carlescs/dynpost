using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Folders;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Environments;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests;

using Secrets = IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>;

public class GlobalVariablesTests
{
    private const string Root = "/work/api";

    private readonly IEnvironmentStore _environments = Substitute.For<IEnvironmentStore>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly ICapturedValueStore _captures = Substitute.For<ICapturedValueStore>();
    private readonly IWorkspaceStateStore _state = Substitute.For<IWorkspaceStateStore>();
    private readonly IWorkspaceVariablesStore _globals = Substitute.For<IWorkspaceVariablesStore>();
    private readonly CurrentWorkspace _current = new();
    private Secrets _storedSecrets = new Dictionary<string, IReadOnlyDictionary<string, string>>();

    public GlobalVariablesTests()
    {
        _current.Open(Root);
        _state.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(WorkspaceState.Default);
        _secrets.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(_ => Result<Secrets>.Success(_storedSecrets));
        _secrets.SaveAsync(Root, Arg.Any<Secrets>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _storedSecrets = call.Arg<Secrets>();
            return Unit.Value;
        });
        _captures.LoadAsync(Root, Arg.Any<CancellationToken>())
            .Returns(Result<Secrets>.Success(new Dictionary<string, IReadOnlyDictionary<string, string>>()));
        _globals.WriteAsync(Root, Arg.Any<IReadOnlyList<EnvironmentVariable>>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
    }

    private ActiveEnvironment Active() => new(_environments, _secrets, _captures, _state, _current, _globals);

    private void GivenGlobals(params EnvironmentVariable[] variables) =>
        _globals.ReadAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<EnvironmentVariable>>.Success(variables));

    private void GivenSelected(EnvironmentDefinition environment)
    {
        _state.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(new WorkspaceState("dev.yaml"));
        _environments.Exists(Root, "dev.yaml").Returns(true);
        _environments.ReadAsync(Root, "dev.yaml", Arg.Any<CancellationToken>()).Returns(environment);
    }

    [Fact]
    public async Task Globals_ApplyWithoutASelectedEnvironment()
    {
        GivenGlobals(new EnvironmentVariable("host", "api.test"), new EnvironmentVariable("token", "", Secret: true));
        _storedSecrets = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [GlobalVariables.SecretsKey] = new Dictionary<string, string> { ["token"] = "s3cr3t" },
        };

        var variables = (await Active().LoadAsync()).Value;

        Assert.Null(variables.EnvironmentName);
        Assert.Equal("api.test", variables.Values["host"]);
        Assert.Equal("s3cr3t", variables.Values["token"]);
        Assert.Equal(["s3cr3t"], variables.SecretValues);
        Assert.Equal(["host", "token"], variables.Globals.Order());
    }

    [Fact]
    public async Task TheEnvironmentWinsAndCanUseGlobals()
    {
        GivenGlobals(new EnvironmentVariable("host", "global.test"), new EnvironmentVariable("version", "v1"));
        GivenSelected(new EnvironmentDefinition("Dev", [new EnvironmentVariable("host", "dev.test"), new EnvironmentVariable("baseUrl", "https://{{host}}/{{version}}")]));

        var variables = (await Active().LoadAsync()).Value;

        Assert.Equal("https://dev.test/v1", variables.Values["baseUrl"]);
        Assert.Equal(["version"], variables.Globals);
    }

    [Fact]
    public async Task UnsetGlobalSecretsAreReportedAsGlobals()
    {
        GivenGlobals(new EnvironmentVariable("token", "", Secret: true));

        var variables = (await Active().LoadAsync()).Value;

        Assert.Contains("token", variables.UnsetSecrets);
        Assert.Contains("Globals", variables.MissingVariablesHint(["token"]));
    }

    [Fact]
    public void FolderVariablesWinOverGlobalsButNotOverTheEnvironment()
    {
        var inherited = InheritedDefaults.From(
        [
            (WorkspacePath.Parse("requests"), new FolderDefaults(variables: [new KeyValueEntry("host", "folder.test"), new KeyValueEntry("env", "folder")])),
        ]);
        var environment = new ActiveVariables("Dev", new Dictionary<string, string> { ["host"] = "global.test", ["env"] = "dev" }, new HashSet<string>())
        {
            Globals = new HashSet<string> { "host" },
        };

        var resolved = FolderInheritance.WithVariables(inherited, environment).Value;

        Assert.Equal("folder.test", resolved.Values["host"]);
        Assert.Equal("dev", resolved.Values["env"]);
    }

    [Fact]
    public async Task Save_WritesTheFileAndKeepsSecretValuesLocal()
    {
        var result = await new SaveGlobalVariables(_globals, _secrets, _current).ExecuteAsync(
            new GlobalVariablesDto([new EnvironmentVariableDto("host", "api.test"), new EnvironmentVariableDto("token", "s3cr3t", Secret: true)]));

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _globals.Received(1).WriteAsync(
            Root,
            Arg.Is<IReadOnlyList<EnvironmentVariable>>(v => v.Count == 2 && v.Single(x => x.Secret).Value == "s3cr3t"),
            Arg.Any<CancellationToken>());
        Assert.Equal("s3cr3t", _storedSecrets[GlobalVariables.SecretsKey]["token"]);
    }
}
