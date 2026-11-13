using Dynpost.Infrastructure.Git;

namespace Dynpost.Infrastructure.Tests.Git;

/// <summary>Runs <see cref="FileSystemWorkingTreeWatcher"/> against real temporary folders and repositories.</summary>
public sealed class FileSystemWorkingTreeWatcherTests : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(800);

    private readonly TempDirectory _temp = new();
    private readonly FileSystemWorkingTreeWatcher _watcher = new(Settle);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task EditingAFile_IsReported()
    {
        var workspace = Repository("api");
        using var changes = new Changes();
        using var watch = _watcher.Watch(workspace, changes.Report);

        await File.WriteAllTextAsync(Path.Combine(workspace, "requests", "health.yaml"), "name: Health\n");

        Assert.True(await changes.NextAsync(Wait));
    }

    [Fact]
    public async Task ABurstOfChanges_IsReportedTogether()
    {
        var workspace = Repository("api");
        using var changes = new Changes();
        using var watch = new FileSystemWorkingTreeWatcher(TimeSpan.FromMilliseconds(500)).Watch(workspace, changes.Report);

        const int writes = 10;
        for (var i = 0; i < writes; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(workspace, "requests", $"r{i}.yaml"), "name: R\n");
        }

        Assert.True(await changes.NextAsync(Wait));
        await Task.Delay(Quiet);
        // Usually once; file system notifications that arrive late may add a second report.
        Assert.True(changes.Pending < writes - 1, $"{changes.Pending + 1} reports for {writes} writes");
    }

    [Fact]
    public async Task ChangesInTheLocalFolder_AreIgnored()
    {
        var workspace = Repository("api");
        Directory.CreateDirectory(Path.Combine(workspace, ".dynpost", "history"));
        using var changes = new Changes();
        using var watch = _watcher.Watch(workspace, changes.Report);

        await File.WriteAllTextAsync(Path.Combine(workspace, ".dynpost", "history", "h.json"), "{}");

        Assert.False(await changes.NextAsync(Quiet));
    }

    [Fact]
    public async Task ACommitMadeWithOtherGitTools_IsReported()
    {
        var workspace = Repository("api");
        File.WriteAllText(Path.Combine(workspace, "requests", "health.yaml"), "name: Health\n");
        TestGit.Run(workspace, "add", ".");
        using var changes = new Changes();
        using var watch = _watcher.Watch(workspace, changes.Report);

        TestGit.Run(workspace, "commit", "-m", "Add health");

        Assert.True(await changes.NextAsync(Wait));
    }

    [Fact]
    public async Task AWorkspaceInsideARepository_SeesCommitsInTheRepositorysGitFolder()
    {
        var repository = Repository("repo");
        var workspace = Path.Combine(repository, "api");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(repository, "other.txt"), "x\n");
        TestGit.Run(repository, "add", ".");
        using var changes = new Changes();
        using var watch = _watcher.Watch(workspace, changes.Report);

        TestGit.Run(repository, "commit", "-m", "Elsewhere");

        Assert.True(await changes.NextAsync(Wait));
    }

    [Fact]
    public async Task Disposing_StopsReporting()
    {
        var workspace = Repository("api");
        using var changes = new Changes();
        var watch = _watcher.Watch(workspace, changes.Report);

        watch.Dispose();
        await File.WriteAllTextAsync(Path.Combine(workspace, "requests", "health.yaml"), "name: Health\n");

        Assert.False(await changes.NextAsync(Quiet));
    }

    [Theory]
    [InlineData("HEAD", true)]
    [InlineData("index", true)]
    [InlineData("packed-refs", true)]
    [InlineData("MERGE_HEAD", true)]
    [InlineData("refs/heads/main", true)]
    [InlineData("refs\\remotes\\origin\\main", true)]
    [InlineData("index.lock", false)]
    [InlineData("refs/heads/main.lock", false)]
    [InlineData("objects/ab/cdef", false)]
    [InlineData("logs/HEAD", false)]
    [InlineData("FETCH_HEAD", false)]
    public void OnlyFilesThatRecordTheStatusCountInTheGitFolder(string path, bool counts) =>
        Assert.Equal(counts, FileSystemWorkingTreeWatcher.CountsInGitFolder(path));

    [Fact]
    public void ALinkedWorktree_WatchesItsOwnAndTheCommonGitFolder()
    {
        var main = Repository("main");
        File.WriteAllText(Path.Combine(main, "a.txt"), "a\n");
        TestGit.Run(main, "add", ".");
        TestGit.Run(main, "commit", "-m", "Initial");
        var worktree = _temp.Combine("linked");
        TestGit.Run(main, "worktree", "add", "-b", "feature", worktree);

        var folders = FileSystemWorkingTreeWatcher.GitFolders.Find(worktree);

        // Compared by name: git may record the real path of a temp folder reached through a link (macOS).
        Assert.Equal(2, folders.Count);
        Assert.Equal(["worktrees", "linked"], Tail(folders[0], 2));
        Assert.Equal(["main", ".git"], Tail(folders[1], 2));
    }

    private static string[] Tail(string path, int count) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[^count..];

    private string Repository(string name)
    {
        var path = _temp.Combine(name);
        Directory.CreateDirectory(Path.Combine(path, "requests"));
        TestGit.Run(path, "init", "-q");
        TestGit.Configure(path);
        return path;
    }

    /// <summary>Counts reported changes so a test can wait for the next one.</summary>
    private sealed class Changes : IDisposable
    {
        private readonly SemaphoreSlim _reported = new(0);

        public void Report() => _reported.Release();

        public Task<bool> NextAsync(TimeSpan timeout) => _reported.WaitAsync(timeout);

        /// <summary>Reports not waited for yet.</summary>
        public int Pending => _reported.CurrentCount;

        public void Dispose() => _reported.Dispose();
    }
}
