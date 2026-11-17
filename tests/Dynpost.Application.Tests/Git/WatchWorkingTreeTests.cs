using Dynpost.Application.Common;
using Dynpost.Application.Git;
using Dynpost.Application.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests.Git;

public class WatchWorkingTreeTests
{
    private readonly IWorkingTreeWatcher _watcher = Substitute.For<IWorkingTreeWatcher>();
    private readonly CurrentWorkspace _workspace = new();
    private readonly IWorkspaceLayoutStore _layout = Substitute.For<IWorkspaceLayoutStore>();
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly LowerCaseSystemFolders _lowerCase;

    public WatchWorkingTreeTests() => _lowerCase = new LowerCaseSystemFolders(_layout, _git);

    [Fact]
    public void WithoutAWorkspace_WatchesNothingUntilOneIsOpened()
    {
        using var watch = new WatchWorkingTree(_workspace, _watcher, _lowerCase);
        watch.Start(() => { }, _ => { });

        _watcher.DidNotReceiveWithAnyArgs().Watch(default!, default!);

        _workspace.Open("/work/api");

        _watcher.Received(1).Watch("/work/api", Arg.Any<Action<WorkingTreeChange>>());
    }

    [Fact]
    public void WatchesTheOpenWorkspaceAndReportsItsChanges()
    {
        var report = Watching("/work/api", out var git, out var files);

        report(new WorkingTreeChange(["requests/health.yaml"]));

        Assert.Equal(1, git.Count);
        Assert.Equal([["requests/health.yaml"]], files);
    }

    [Fact]
    public void ChangesInASystemFolderSpelledInAnotherCase_LowerCaseIt()
    {
        _layout.FindMiscasedSystemFolders("/work/api").Returns(Result<IReadOnlyList<MiscasedSystemFolder>>.Success(
            [new MiscasedSystemFolder("Requests", "requests")]));
        _git.MoveTrackedFolderAsync("/work/api", "Requests", "requests", Arg.Any<CancellationToken>()).Returns(true);
        var report = Watching("/work/api", out _, out _);

        report(new WorkingTreeChange(["requests/health.yaml", "README.md"]));
        _layout.DidNotReceiveWithAnyArgs().FindMiscasedSystemFolders(default!);

        report(new WorkingTreeChange(["Requests/health.yaml"]));
        _git.Received(1).MoveTrackedFolderAsync("/work/api", "Requests", "requests", Arg.Any<CancellationToken>());

        report(new WorkingTreeChange(null));
        _layout.Received(2).FindMiscasedSystemFolders("/work/api");
    }

    [Fact]
    public void OnlyGitStateChanged_ReportsNoFiles()
    {
        var report = Watching("/work/api", out var git, out var files);

        report(new WorkingTreeChange([]));

        Assert.Equal(1, git.Count);
        Assert.Empty(files);
    }

    [Fact]
    public void FilesTheAppDoesNotRead_AreLeftOut()
    {
        var report = Watching("/work/api", out var git, out var files);

        report(new WorkingTreeChange(["README.md", "data/users.csv", "environments/dev.yaml", "plans/load.run.yaml"]));
        report(new WorkingTreeChange(["notes/todo.txt"]));

        Assert.Equal(2, git.Count);
        Assert.Equal([["environments/dev.yaml", "plans/load.run.yaml"]], files);
    }

    [Fact]
    public void LostChanges_ReportThatAnyFileMayHaveChanged()
    {
        var report = Watching("/work/api", out _, out var files);

        report(new WorkingTreeChange(null));

        Assert.Equal([null], files);
    }

    [Theory]
    [InlineData("requests", true)]
    [InlineData("requests/users/_folder.yaml", true)]
    [InlineData("plans/load.run.yaml", true)]
    [InlineData("environments/dev.yaml", true)]
    [InlineData("dynpost.yaml", true)]
    [InlineData(".gitignore", true)]
    [InlineData("requests-old/a.yaml", false)]
    [InlineData("docs/dynpost.yaml", false)]
    [InlineData("README.md", false)]
    public void IsAppFile_CoversTheFilesTheUiShows(string path, bool expected) =>
        Assert.Equal(expected, WatchWorkingTree.IsAppFile(path));

    [Fact]
    public void OpeningAnotherWorkspace_StopsWatchingThePreviousOne()
    {
        var first = Substitute.For<IDisposable>();
        var second = Substitute.For<IDisposable>();
        _watcher.Watch("/work/api", Arg.Any<Action<WorkingTreeChange>>()).Returns(first);
        _watcher.Watch("/work/web", Arg.Any<Action<WorkingTreeChange>>()).Returns(second);
        using var watch = new WatchWorkingTree(_workspace, _watcher, _lowerCase);
        watch.Start(() => { }, _ => { });

        _workspace.Open("/work/api");
        _workspace.Open("/work/web");

        first.Received(1).Dispose();
        second.DidNotReceive().Dispose();
    }

    [Fact]
    public void Dispose_StopsWatching()
    {
        var handle = Substitute.For<IDisposable>();
        _watcher.Watch("/work/api", Arg.Any<Action<WorkingTreeChange>>()).Returns(handle);
        var watch = new WatchWorkingTree(_workspace, _watcher, _lowerCase);
        watch.Start(() => { }, _ => { });
        _workspace.Open("/work/api");

        watch.Dispose();
        _workspace.Open("/work/web");

        handle.Received(1).Dispose();
        _watcher.DidNotReceive().Watch("/work/web", Arg.Any<Action<WorkingTreeChange>>());
    }

    [Fact]
    public void AFolderThatCannotBeWatched_IsNotAnError()
    {
        _watcher.Watch("/work/api", Arg.Any<Action<WorkingTreeChange>>()).Returns(_ => throw new IOException("Too many watches."));
        using var watch = new WatchWorkingTree(_workspace, _watcher, _lowerCase);
        watch.Start(() => { }, _ => { });

        _workspace.Open("/work/api");

        _watcher.Received(1).Watch("/work/api", Arg.Any<Action<WorkingTreeChange>>());
    }

    /// <summary>Opens and watches <paramref name="root"/>; returns what the watcher reports changes to.</summary>
    private Action<WorkingTreeChange> Watching(string root, out List<int> git, out List<IReadOnlyList<string>?> files)
    {
        _workspace.Open(root);
        Action<WorkingTreeChange>? reported = null;
        _watcher.Watch(root, Arg.Do<Action<WorkingTreeChange>>(a => reported = a)).Returns(Substitute.For<IDisposable>());
        var gitChanges = new List<int>();
        var fileChanges = new List<IReadOnlyList<string>?>();
        var watch = new WatchWorkingTree(_workspace, _watcher, _lowerCase);
        watch.Start(() => gitChanges.Add(gitChanges.Count), paths => fileChanges.Add(paths));
        git = gitChanges;
        files = fileChanges;
        return reported!;
    }
}
