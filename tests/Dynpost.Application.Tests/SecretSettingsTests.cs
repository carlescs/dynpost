using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Secrets;
using Dynpost.Application.Settings;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Environments;
using NSubstitute;

namespace Dynpost.Application.Tests;

using Secrets = IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>;

public class SecretSettingsTests
{
    private const string Root = "/work/api";
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly IKeychain _keychain = Substitute.For<IKeychain>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly CurrentWorkspace _current = new();
    private AppSettings _saved = AppSettings.Default;

    public SecretSettingsTests()
    {
        _current.Open(Root);
        _keychain.Name.Returns("Test keychain");
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => _saved);
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _saved = call.Arg<Func<AppSettings, AppSettings>>()(_saved);
            return Result<AppSettings>.Success(_saved);
        });
        _secrets.LoadAsync(Root, Arg.Any<CancellationToken>())
            .Returns(Result<Secrets>.Success(new Dictionary<string, IReadOnlyDictionary<string, string>>()));
    }

    [Fact]
    public async Task Set_SavesTheChoiceAndMovesTheOpenWorkspacesSecrets()
    {
        _keychain.IsAvailable.Returns(true);

        var result = await new SetSecretSettings(_settings, _keychain, _secrets, _current).ExecuteAsync(SecretStorages.Keychain, useDotEnv: true);

        Assert.Equal(new SecretSettingsDto(SecretStorages.Keychain, true, true, "Test keychain"), result.Value);
        Assert.Equal(new SecretSettings(SecretStorages.Keychain, true), _saved.Secrets);
        await _secrets.Received(1).LoadAsync(Root, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_RefusesAKeychainThatIsNotAvailableAndUnknownStores()
    {
        _keychain.IsAvailable.Returns(false);
        var useCase = new SetSecretSettings(_settings, _keychain, _secrets, _current);

        Assert.False((await useCase.ExecuteAsync(SecretStorages.Keychain, false)).IsSuccess);
        Assert.False((await useCase.ExecuteAsync("cloud", false)).IsSuccess);
        Assert.Null(_saved.Secrets);
    }

    [Fact]
    public async Task Get_DefaultsToTheFile()
    {
        var result = await new GetSecretSettings(_settings, _keychain).ExecuteAsync();

        Assert.Equal(SecretStorages.File, result.Value.Storage);
        Assert.False(result.Value.UseDotEnv);
    }

    [Fact]
    public async Task SecretsWithoutAValue_TakeOneFromDotEnv()
    {
        var environments = Substitute.For<IEnvironmentStore>();
        var captures = Substitute.For<ICapturedValueStore>();
        var state = Substitute.For<IWorkspaceStateStore>();
        var dotEnv = Substitute.For<IDotEnvValues>();
        state.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(new WorkspaceState("dev.yaml"));
        environments.Exists(Root, "dev.yaml").Returns(true);
        environments.ReadAsync(Root, "dev.yaml", Arg.Any<CancellationToken>())
            .Returns(new EnvironmentDefinition("Dev", [new EnvironmentVariable("token", "", Secret: true), new EnvironmentVariable("other", "", Secret: true)]));
        captures.LoadAsync(Root, Arg.Any<CancellationToken>())
            .Returns(Result<Secrets>.Success(new Dictionary<string, IReadOnlyDictionary<string, string>>()));
        dotEnv.ReadAsync(Root, Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string> { ["token"] = "from-env" });

        var variables = (await new ActiveEnvironment(environments, _secrets, captures, state, _current, dotEnv: dotEnv).LoadAsync()).Value;

        Assert.Equal("from-env", variables.Values["token"]);
        Assert.Contains("from-env", variables.SecretValues!);
        Assert.Contains("other", variables.UnsetSecrets);
    }
}
