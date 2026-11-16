using Dynpost.Application.Common;
using Dynpost.Application.Secrets;
using Dynpost.Application.Settings;
using Dynpost.Infrastructure.Environments;
using Dynpost.Infrastructure.Secrets;
using NSubstitute;

namespace Dynpost.Infrastructure.Tests.Secrets;

using Secrets = IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>;

public sealed class SecretStorageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dynpost-tests-").FullName;
    private readonly FakeKeychain _keychain = new();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Secrets Of(string id, string name, string value) =>
        new Dictionary<string, IReadOnlyDictionary<string, string>> { [id] = new Dictionary<string, string> { [name] = value } };

    private void Choose(string storage, bool useDotEnv = false) =>
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(AppSettings.Default with { Secrets = new SecretSettings(storage, useDotEnv) });

    [Fact]
    public async Task Keychain_KeepsValuesInTheCredentialStoreAndOnlyNamesOnDisk()
    {
        var store = new KeychainSecretStore(_keychain);

        Assert.True((await store.SaveAsync(_root, Of("dev.yaml", "token", "s3cr3t"))).IsSuccess);

        Assert.Equal("s3cr3t", Assert.Single(_keychain.Values).Value);
        var index = File.ReadAllText(Path.Combine(_root, ".dynpost", KeychainSecretStore.IndexFileName));
        Assert.Contains("token", index);
        Assert.DoesNotContain("s3cr3t", index);
        Assert.Equal("s3cr3t", (await new KeychainSecretStore(_keychain).LoadAsync(_root)).Value["dev.yaml"]["token"]);

        Assert.True((await store.SaveAsync(_root, new Dictionary<string, IReadOnlyDictionary<string, string>>())).IsSuccess);
        Assert.Empty(_keychain.Values);
        Assert.False(KeychainSecretStore.HasIndex(_root));
    }

    [Fact]
    public async Task Selected_MovesTheSecretsToTheChosenStoreOnFirstRead()
    {
        var file = new YamlSecretStore();
        await file.SaveAsync(_root, Of("dev.yaml", "token", "s3cr3t"));
        var selected = new SelectedSecretStore(file, new KeychainSecretStore(_keychain), _keychain, _settings);

        Choose(SecretStorages.Keychain);
        Assert.Equal("s3cr3t", (await selected.LoadAsync(_root)).Value["dev.yaml"]["token"]);
        Assert.False(File.Exists(Path.Combine(_root, ".dynpost", YamlSecretStore.FileName)));
        Assert.Single(_keychain.Values);

        Choose(SecretStorages.File);
        Assert.Equal("s3cr3t", (await selected.LoadAsync(_root)).Value["dev.yaml"]["token"]);
        Assert.Empty(_keychain.Values);
        Assert.True(File.Exists(Path.Combine(_root, ".dynpost", YamlSecretStore.FileName)));
    }

    [Fact]
    public async Task Selected_UsesTheFileWhenTheKeychainIsNotAvailable()
    {
        _keychain.Available = false;
        Choose(SecretStorages.Keychain);
        var selected = new SelectedSecretStore(new YamlSecretStore(), new KeychainSecretStore(_keychain), _keychain, _settings);

        await selected.SaveAsync(_root, Of("dev.yaml", "token", "s3cr3t"));

        Assert.True(File.Exists(Path.Combine(_root, ".dynpost", YamlSecretStore.FileName)));
    }

    [Fact]
    public async Task DotEnv_IsReadOnlyWhenTurnedOn()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), "token=abc\n");
        var values = new DotEnvValues(_settings);

        Choose(SecretStorages.File, useDotEnv: false);
        Assert.Empty(await values.ReadAsync(_root));

        Choose(SecretStorages.File, useDotEnv: true);
        Assert.Equal("abc", (await values.ReadAsync(_root))["token"]);
    }

    [Fact]
    public async Task DotEnv_IsCopiedToAnotherFolder_UnlessItHasOne()
    {
        var other = Directory.CreateTempSubdirectory("dynpost-tests-").FullName;
        try
        {
            var values = new DotEnvValues(_settings);
            Assert.False((await values.CopyAsync(_root, other)).Value);

            File.WriteAllText(Path.Combine(_root, ".env"), "token=abc\n");
            Assert.True((await values.CopyAsync(_root, other)).Value);
            Assert.Equal("token=abc\n", File.ReadAllText(Path.Combine(other, ".env")));

            File.WriteAllText(Path.Combine(_root, ".env"), "token=changed\n");
            Assert.False((await values.CopyAsync(_root, other)).Value);
            Assert.Equal("token=abc\n", File.ReadAllText(Path.Combine(other, ".env")));
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void DotEnv_ParsesQuotesCommentsAndExport()
    {
        var values = DotEnvValues.Parse("""
            # comment
            export API_KEY=abc123
            PLAIN=value # trailing comment
            SINGLE='a "quoted" $value'
            DOUBLE="line one\nline \"two\""
            MULTI="first
            second"
            EMPTY=
            BAD LINE
            API_KEY=override
            """);

        Assert.Equal("override", values["API_KEY"]);
        Assert.Equal("value", values["PLAIN"]);
        Assert.Equal("a \"quoted\" $value", values["SINGLE"]);
        Assert.Equal("line one\nline \"two\"", values["DOUBLE"]);
        Assert.Equal("first\nsecond", values["MULTI"]);
        Assert.Equal(string.Empty, values["EMPTY"]);
        Assert.False(values.ContainsKey("BAD LINE"));
    }

    private sealed class FakeKeychain : IKeychain
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public bool Available { get; set; } = true;

        public bool IsAvailable => Available;

        public string Name => "Fake keychain";

        public Task<Result<string?>> GetAsync(string account, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string?>.Success(Values.GetValueOrDefault(account)));

        public Task<Result<Unit>> SetAsync(string account, string value, CancellationToken cancellationToken = default)
        {
            Values[account] = value;
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public Task<Result<Unit>> DeleteAsync(string account, CancellationToken cancellationToken = default)
        {
            Values.Remove(account);
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }
    }
}
