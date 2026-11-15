using Dynpost.Domain.Environments;
using Dynpost.Infrastructure.Workspaces;

namespace Dynpost.Infrastructure.Tests.Workspaces;

public sealed class YamlWorkspaceVariablesStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("dynpost-tests-").FullName;
    private readonly YamlWorkspaceVariablesStore _store = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string FilePath => Path.Combine(_root, "dynpost.yaml");

    [Fact]
    public async Task Write_KeepsTheOtherKeysAndReplacesTheGlobals()
    {
        File.WriteAllText(FilePath, "# workspace\nlayout: 2\nvariables:\n  old: x\nsecrets:\n  - gone\n");

        var written = await _store.WriteAsync(_root, [new EnvironmentVariable("host", "api.test"), new EnvironmentVariable("token", "s3cr3t", Secret: true)]);

        Assert.True(written.Value);
        Assert.Equal("# workspace\nlayout: 2\nvariables:\n  host: api.test\nsecrets:\n  - token\n", File.ReadAllText(FilePath));
        var read = (await _store.ReadAsync(_root)).Value;
        Assert.Equal([new EnvironmentVariable("host", "api.test"), new EnvironmentVariable("token", "", Secret: true)], read);

        Assert.False((await _store.WriteAsync(_root, read)).Value);
        Assert.True((await _store.WriteAsync(_root, [])).Value);
        Assert.Equal("# workspace\nlayout: 2\n", File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task Read_WithoutAFile_HasNoGlobals()
    {
        Assert.Empty((await _store.ReadAsync(_root)).Value);
        Assert.False((await _store.WriteAsync(_root, [])).Value);
        Assert.False(File.Exists(FilePath));
    }
}
