using Missive.Application.Common;
using Missive.Application.OAuth2;
using Missive.Infrastructure.OAuth2;

namespace Missive.Infrastructure.Tests.OAuth2;

public sealed class JsonOAuth2TokenStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("missive-tests-").FullName;
    private readonly JsonOAuth2TokenStore _store = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string FilePath => Path.Combine(_root, ".missive", JsonOAuth2TokenStore.FileName);

    private static readonly OAuth2Token Token = new(
        "https://auth.test/token", "app", "read", "access", "refresh",
        new DateTimeOffset(2026, 11, 7, 13, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Tokens_RoundTripSortedAndGitIgnored()
    {
        var tokens = new Dictionary<string, IReadOnlyDictionary<string, OAuth2Token>>
        {
            ["prod.yaml"] = new Dictionary<string, OAuth2Token> { ["b"] = Token with { RefreshToken = null, Expires = null } },
            ["dev.yaml"] = new Dictionary<string, OAuth2Token> { ["z"] = Token, ["a"] = Token },
            [""] = new Dictionary<string, OAuth2Token> { ["c"] = Token },
            ["empty.yaml"] = new Dictionary<string, OAuth2Token>(),
        };

        Assert.True((await _store.SaveAsync(_root, tokens)).IsSuccess);
        var loaded = (await _store.LoadAsync(_root)).Value;

        Assert.Equal(["", "dev.yaml", "prod.yaml"], loaded.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(Token, loaded["dev.yaml"]["a"]);
        Assert.Equal(tokens["prod.yaml"]["b"], loaded["prod.yaml"]["b"]);
        Assert.Equal(".missive/\n", File.ReadAllText(Path.Combine(_root, ".gitignore")));
        var json = File.ReadAllText(FilePath);
        Assert.True(json.IndexOf("\"a\"", StringComparison.Ordinal) < json.IndexOf("\"z\"", StringComparison.Ordinal));
        Assert.DoesNotContain("refreshToken\": null", json);
        Assert.EndsWith("}\n", json);
    }

    [Fact]
    public async Task Save_WithoutTokens_RemovesTheFile()
    {
        await _store.SaveAsync(_root, new Dictionary<string, IReadOnlyDictionary<string, OAuth2Token>>
        {
            ["dev.yaml"] = new Dictionary<string, OAuth2Token> { ["a"] = Token },
        });

        await _store.SaveAsync(_root, new Dictionary<string, IReadOnlyDictionary<string, OAuth2Token>>());

        Assert.False(File.Exists(FilePath));
        Assert.Empty((await _store.LoadAsync(_root)).Value);
    }

    [Fact]
    public async Task Load_InvalidFile_IsAnError()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "[1, 2");

        Assert.Equal(ErrorCodes.InvalidFile, (await _store.LoadAsync(_root)).Error?.Code);
    }
}
