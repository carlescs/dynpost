using Dynypost.Application.Common;
using Dynypost.Domain.Requests;
using Dynypost.Domain.Workspaces;
using Dynypost.Infrastructure.Workspaces;

namespace Dynypost.Infrastructure.Tests.Workspaces;

public sealed class YamlFolderDefaultsStoreTests : IDisposable
{
    private static readonly WorkspacePath Users = WorkspacePath.Parse("requests/users");

    private readonly string _root = Directory.CreateTempSubdirectory("dynypost-tests-").FullName;
    private readonly YamlFolderDefaultsStore _store = new();

    public YamlFolderDefaultsStoreTests() => Directory.CreateDirectory(Path.Combine(_root, "requests", "users"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string FilePath() => Path.Combine(_root, "requests", "users", "_folder.yaml");

    [Fact]
    public async Task Read_AFolderWithoutAFileSetsNothing()
    {
        var result = await _store.ReadAsync(_root, Users);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsEmpty);
    }

    [Fact]
    public async Task Read_ReportsAnInvalidFile()
    {
        await File.WriteAllTextAsync(FilePath(), "headers: [oops");

        var result = await _store.ReadAsync(_root, Users);

        Assert.Equal(ErrorCodes.InvalidFile, result.Error?.Code);
        Assert.Contains("requests/users/_folder.yaml", result.Error!.Message);
    }

    [Fact]
    public async Task Write_CreatesTheFileAndReadsItBack()
    {
        var defaults = new FolderDefaults([new KeyValueEntry("Accept", "application/json")], new BearerAuth("{{token}}"));

        var written = await _store.WriteAsync(_root, Users, defaults);
        var read = await _store.ReadAsync(_root, Users);

        Assert.True(written.Value);
        Assert.Equal("headers:\n  Accept: application/json\nauth:\n  type: bearer\n  token: \"{{token}}\"\n", await File.ReadAllTextAsync(FilePath()));
        Assert.Equal(defaults.Headers, read.Value.Headers);
        Assert.Equal(defaults.Auth, read.Value.Auth);
    }

    [Fact]
    public async Task Write_LeavesAnEquivalentFileUntouched()
    {
        const string handFormatted = "# shared headers\nheaders: { Accept: application/json }\n";
        await File.WriteAllTextAsync(FilePath(), handFormatted);

        var written = await _store.WriteAsync(_root, Users, new FolderDefaults([new KeyValueEntry("Accept", "application/json")]));

        Assert.False(written.Value);
        Assert.Equal(handFormatted, await File.ReadAllTextAsync(FilePath()));
    }

    [Fact]
    public async Task Write_RemovesTheFileWhenTheFolderSetsNothing()
    {
        await File.WriteAllTextAsync(FilePath(), "headers:\n  Accept: application/json\n");

        var written = await _store.WriteAsync(_root, Users, FolderDefaults.Empty);
        var again = await _store.WriteAsync(_root, Users, FolderDefaults.Empty);

        Assert.True(written.Value);
        Assert.False(again.Value);
        Assert.False(File.Exists(FilePath()));
    }
}
