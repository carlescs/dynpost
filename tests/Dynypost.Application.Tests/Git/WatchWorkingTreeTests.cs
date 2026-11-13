using Dynypost.Application.Git;
using Dynypost.Application.Workspaces;
using NSubstitute;

namespace Dynypost.Application.Tests.Git;

public class WatchWorkingTreeTests
{
    private readonly IWorkingTreeWatcher _watcher = Substitute.For<IWorkingTreeWatcher>();
    private readonly CurrentWorkspace _workspace = new();

    [Fact]
    public void WithoutAWorkspace_WatchesNothingUntilOneIsOpened()
    {
        using var watch = new WatchWorkingTree(_workspace, _watcher);
        watch.Start(() => { });

        _watcher.DidNotReceiveWithAnyArgs().Watch(default!, default!);

        _workspace.Open("/work/api");

        _watcher.Received(1).Watch("/work/api", Arg.Any<Action>());
    }

    [Fact]
    public void WatchesTheOpenWorkspaceAndReportsItsChanges()
    {
        _workspace.Open("/work/api");
        Action? reported = null;
        _watcher.Watch("/work/api", Arg.Do<Action>(a => reported = a)).Returns(Substitute.For<IDisposable>());
        var changes = 0;

        using var watch = new WatchWorkingTree(_workspace, _watcher);
        watch.Start(() => changes++);
        reported!();

        Assert.Equal(1, changes);
    }

    [Fact]
    public void OpeningAnotherWorkspace_StopsWatchingThePreviousOne()
    {
        var first = Substitute.For<IDisposable>();
        var second = Substitute.For<IDisposable>();
        _watcher.Watch("/work/api", Arg.Any<Action>()).Returns(first);
        _watcher.Watch("/work/web", Arg.Any<Action>()).Returns(second);
        using var watch = new WatchWorkingTree(_workspace, _watcher);
        watch.Start(() => { });

        _workspace.Open("/work/api");
        _workspace.Open("/work/web");

        first.Received(1).Dispose();
        second.DidNotReceive().Dispose();
    }

    [Fact]
    public void Dispose_StopsWatching()
    {
        var handle = Substitute.For<IDisposable>();
        _watcher.Watch("/work/api", Arg.Any<Action>()).Returns(handle);
        var watch = new WatchWorkingTree(_workspace, _watcher);
        watch.Start(() => { });
        _workspace.Open("/work/api");

        watch.Dispose();
        _workspace.Open("/work/web");

        handle.Received(1).Dispose();
        _watcher.DidNotReceive().Watch("/work/web", Arg.Any<Action>());
    }

    [Fact]
    public void AFolderThatCannotBeWatched_IsNotAnError()
    {
        _watcher.Watch("/work/api", Arg.Any<Action>()).Returns(_ => throw new IOException("Too many watches."));
        using var watch = new WatchWorkingTree(_workspace, _watcher);
        watch.Start(() => { });

        _workspace.Open("/work/api");

        _watcher.Received(1).Watch("/work/api", Arg.Any<Action>());
    }
}
