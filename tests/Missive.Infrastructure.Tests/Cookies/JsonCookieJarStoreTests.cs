using Missive.Application.Common;
using Missive.Domain.Http;
using Missive.Infrastructure.Cookies;

namespace Missive.Infrastructure.Tests.Cookies;

public sealed class JsonCookieJarStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("missive-tests-").FullName;
    private readonly JsonCookieJarStore _store = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string FilePath => Path.Combine(_root, ".missive", JsonCookieJarStore.FileName);

    [Fact]
    public async Task Jars_RoundTripAndAreGitIgnored()
    {
        var jars = new Dictionary<string, IReadOnlyList<StoredCookie>>
        {
            ["prod.yaml"] = [new StoredCookie("a", "1", "api.example.com", "/")],
            ["dev.yaml"] = [new StoredCookie("session", "x; y", ".localhost", "/api", new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero), true, true)],
            ["empty.yaml"] = [],
        };

        Assert.True((await _store.SaveAsync(_root, jars)).IsSuccess);
        var loaded = (await _store.LoadAsync(_root)).Value;

        Assert.Equal(["dev.yaml", "prod.yaml"], loaded.Keys.Order());
        Assert.Equal(jars["dev.yaml"], loaded["dev.yaml"]);
        Assert.Equal(jars["prod.yaml"], loaded["prod.yaml"]);
        Assert.Equal(".missive/\n", File.ReadAllText(Path.Combine(_root, ".gitignore")));
        var json = File.ReadAllText(FilePath);
        Assert.EndsWith("}\n", json);
        Assert.DoesNotContain("\r", json);
        Assert.DoesNotContain("\"secure\": false", json);
    }

    [Fact]
    public async Task Save_WithoutCookies_RemovesTheFile()
    {
        await _store.SaveAsync(_root, new Dictionary<string, IReadOnlyList<StoredCookie>> { ["dev.yaml"] = [new StoredCookie("a", "1", "x.test", "/")] });

        await _store.SaveAsync(_root, new Dictionary<string, IReadOnlyList<StoredCookie>>());

        Assert.False(File.Exists(FilePath));
        Assert.Empty((await _store.LoadAsync(_root)).Value);
    }

    [Fact]
    public async Task Load_SkipsUnusableEntries()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ \"dev.yaml\": [null, { \"name\": \"\" }, { \"name\": \"a\", \"domain\": \"x.test\" }], \"prod.yaml\": null }");

        var loaded = (await _store.LoadAsync(_root)).Value;

        Assert.Equal(new StoredCookie("a", "", "x.test", "/"), Assert.Single(loaded["dev.yaml"]));
        Assert.Empty(loaded["prod.yaml"]);
    }

    [Fact]
    public async Task Load_InvalidFile_IsReported()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "[not, a, map");

        var result = await _store.LoadAsync(_root);

        Assert.Equal(ErrorCodes.InvalidFile, result.Error?.Code);
    }
}
