using Dynpost.Application.Common;
using Dynpost.Application.History;
using Dynpost.Application.Folders;
using Dynpost.Application.Runs;
using Dynpost.Application.Sharing;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Runs;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests.Runs;

public class RunFolderUseCaseTests
{
    private const string Root = "/work/api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IRunPlanStore _store = Substitute.For<IRunPlanStore>();
    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
    private readonly IHistoryStore _history = Substitute.For<IHistoryStore>();
    private readonly RunRegistry _registry = new();
    private readonly CurrentWorkspace _current = new();

    public RunFolderUseCaseTests()
    {
        _current.Open(Root);
        _repository.CreateFolder(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.CopyFile(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.MoveFolder(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.DeleteFolder(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _localFiles.Move(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _localFiles.Copy(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _store.Move(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _runs.MovePlanAsync(Root, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _runs.DeletePlanAsync(Root, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
    }

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);

    /// <summary>Makes <paramref name="folder"/> an existing folder holding <paramref name="files"/>.</summary>
    private void Folder(string folder, params string[] files)
    {
        _repository.DirectoryExists(Root, P(folder)).Returns(true);
        _repository.ReadFolderContents(Root, P(folder))
            .Returns(new FolderContents(files.Select(P).ToList(), [], []));
    }

    private void Running(string planPath)
    {
        var plan = new RunPlan(
            "Smoke",
            [P("users.yaml")],
            new RunLoad(1, 1, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null),
            null,
            new Dictionary<string, RunAssertion>(),
            RunThresholds.None);
        _registry.Add(new ActiveRun("run-1", Root, planPath, plan, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task List_ShowsTheVisibleFoldersInsideThePlansFolder()
    {
        _repository.DirectoryExists(Root, P("plans")).Returns(true);
        _repository.ReadFolderContents(Root, P("plans")).Returns(new FolderContents(
            [P("plans/perf/smoke.run.yaml")],
            [P("plans/perf/nightly"), P("plans/empty"), P("plans/perf"), P("plans/.hidden"), P("plans/.hidden/inner")],
            []));

        var folders = await new ListRunFolders(_repository, _current).ExecuteAsync();

        Assert.Equal(["plans/empty", "plans/perf", "plans/perf/nightly"], folders.Value);
    }

    [Fact]
    public async Task List_IsEmptyWithoutAPlansFolder()
    {
        var folders = await new ListRunFolders(_repository, _current).ExecuteAsync();

        Assert.Empty(folders.Value);
    }

    [Fact]
    public async Task Rename_MovesTheFolderAndTheRunsOfItsPlans()
    {
        Folder("plans/perf", "plans/perf/smoke.run.yaml", "plans/perf/deep/soak.run.yaml", "plans/perf/_folder.yaml");

        var renamed = await new RenameRunFolder(new RenameFolder(_repository, _localFiles, _history, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("plans/perf", "load");

        Assert.Equal("plans/load", renamed.Value.Path);
        _repository.Received(1).MoveFolder(Root, P("plans/perf"), P("plans/load"));
        await _runs.Received(1).MovePlanAsync(Root, "plans/perf/smoke.run.yaml", "plans/load/smoke.run.yaml", Arg.Any<CancellationToken>());
        await _runs.Received(1).MovePlanAsync(Root, "plans/perf/deep/soak.run.yaml", "plans/load/deep/soak.run.yaml", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Move_RefusesFoldersWithARunningPlan()
    {
        Folder("plans/perf", "plans/perf/smoke.run.yaml");
        Running("plans/perf/smoke.run.yaml");

        var moved = await new MoveRunFolder(new MoveFolder(_repository, _localFiles, _history, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("plans/perf", "plans");

        Assert.Equal(ErrorCodes.InvalidRequest, moved.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().MoveFolder(default!, default!, default!);
    }

    [Fact]
    public async Task Rename_RefusesFoldersOutsideThePlansFolder()
    {
        Folder("requests/shop", "requests/shop/list.yaml");

        var renamed = await new RenameRunFolder(new RenameFolder(_repository, _localFiles, _history, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("requests/shop", "store");

        Assert.Equal(ErrorCodes.InvalidPath, renamed.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().MoveFolder(default!, default!, default!);
    }

    [Fact]
    public async Task Move_MovesTheFolderAndTheRunsOfItsPlans()
    {
        Folder("plans/perf", "plans/perf/smoke.run.yaml");
        Folder("plans/archive");

        var moved = await new MoveRunFolder(new MoveFolder(_repository, _localFiles, _history, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("plans/perf", "plans/archive");

        Assert.Equal("plans/archive/perf", moved.Value.Path);
        await _runs.Received(1).MovePlanAsync(Root, "plans/perf/smoke.run.yaml", "plans/archive/perf/smoke.run.yaml", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Duplicate_CopiesAFolderOfPlans()
    {
        Folder("plans/perf", "plans/perf/smoke.run.yaml");

        var copy = new DuplicateRunFolder(new DuplicateFolder(_repository, _localFiles, _current), _repository, _current)
            .Execute("plans/perf");

        Assert.Equal("plans/perf copy", copy.Value.Path);
        _repository.Received(1).CopyFile(Root, P("plans/perf/smoke.run.yaml"), P("plans/perf copy/smoke.run.yaml"));
    }

    [Fact]
    public async Task Delete_DeletesTheFolderAndTheRunsOfItsPlans()
    {
        Folder("plans/perf", "plans/perf/smoke.run.yaml", "plans/perf/deep/soak.run.yaml");

        var deleted = await new DeleteRunFolder(new DeleteFolder(_repository, _localFiles, _history, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("plans/perf");

        Assert.True(deleted.IsSuccess);
        _repository.Received(1).DeleteFolder(Root, P("plans/perf"));
        await _runs.Received(1).DeletePlanAsync(Root, "plans/perf/smoke.run.yaml", Arg.Any<CancellationToken>());
        await _runs.Received(1).DeletePlanAsync(Root, "plans/perf/deep/soak.run.yaml", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_KeepsTheRunsWhenTheFolderCantBeDeleted()
    {
        Folder("plans/perf", "plans/perf/smoke.run.yaml", "plans/perf/notes.txt");

        var deleted = await new DeleteRunFolder(new DeleteFolder(_repository, _localFiles, _history, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("plans/perf");

        Assert.Equal(ErrorCodes.FolderNotEmpty, deleted.Error?.Code);
        await _runs.DidNotReceiveWithAnyArgs().DeletePlanAsync(default!, default!, default);
    }

    private MoveRunPlan MovePlan() => new(_store, _repository, _runs, _registry, _localFiles, _current);

    [Fact]
    public async Task MovePlan_MovesTheFileWithItsRunsAndSharing()
    {
        _store.Exists(Root, P("plans/smoke.run.yaml")).Returns(true);
        _repository.DirectoryExists(Root, P("plans/perf")).Returns(true);

        var moved = await MovePlan().ExecuteAsync("plans/smoke.run.yaml", "plans/perf");

        Assert.Equal("plans/perf/smoke.run.yaml", moved.Value.Path);
        _store.Received(1).Move(Root, P("plans/smoke.run.yaml"), P("plans/perf/smoke.run.yaml"));
        _localFiles.Received(1).Move(Root, P("plans/smoke.run.yaml"), P("plans/perf/smoke.run.yaml"));
        await _runs.Received(1).MovePlanAsync(Root, "plans/smoke.run.yaml", "plans/perf/smoke.run.yaml", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MovePlan_ToTheRoot()
    {
        _store.Exists(Root, P("plans/perf/smoke.run.yaml")).Returns(true);

        var moved = await MovePlan().ExecuteAsync("plans/perf/smoke.run.yaml", "plans");

        Assert.Equal("plans/smoke.run.yaml", moved.Value.Path);
    }

    [Fact]
    public async Task MovePlan_LeavesAPlanInItsOwnFolderAlone()
    {
        _store.Exists(Root, P("plans/perf/smoke.run.yaml")).Returns(true);
        _repository.DirectoryExists(Root, P("plans/perf")).Returns(true);

        var moved = await MovePlan().ExecuteAsync("plans/perf/smoke.run.yaml", "plans/perf");

        Assert.Equal("plans/perf/smoke.run.yaml", moved.Value.Path);
        _store.DidNotReceiveWithAnyArgs().Move(default!, default!, default!);
    }

    [Fact]
    public async Task MovePlan_RefusesTakenNames()
    {
        _store.Exists(Root, P("plans/smoke.run.yaml")).Returns(true);
        _store.Exists(Root, P("plans/perf/smoke.run.yaml")).Returns(true);
        _repository.DirectoryExists(Root, P("plans/perf")).Returns(true);

        var moved = await MovePlan().ExecuteAsync("plans/smoke.run.yaml", "plans/perf");

        Assert.Equal(ErrorCodes.AlreadyExists, moved.Error?.Code);
        _store.DidNotReceiveWithAnyArgs().Move(default!, default!, default!);
    }

    [Fact]
    public async Task MovePlan_RefusesARunningPlan()
    {
        _store.Exists(Root, P("plans/smoke.run.yaml")).Returns(true);
        _repository.DirectoryExists(Root, P("plans/perf")).Returns(true);
        Running("plans/smoke.run.yaml");

        var moved = await MovePlan().ExecuteAsync("plans/smoke.run.yaml", "plans/perf");

        Assert.Equal(ErrorCodes.InvalidRequest, moved.Error?.Code);
        _store.DidNotReceiveWithAnyArgs().Move(default!, default!, default!);
    }

    [Fact]
    public async Task MovePlan_RefusesPathsThatAreNotPlans()
    {
        var moved = await MovePlan().ExecuteAsync("users/get.yaml", "plans");
        Assert.Equal(ErrorCodes.InvalidPath, moved.Error?.Code);

        var outside = await MovePlan().ExecuteAsync("smoke.run.yaml", "plans");
        Assert.Equal(ErrorCodes.InvalidPath, outside.Error?.Code);

        _store.Exists(Root, P("plans/smoke.run.yaml")).Returns(true);
        var intoRequests = await MovePlan().ExecuteAsync("plans/smoke.run.yaml", "requests");
        Assert.Equal(ErrorCodes.InvalidPath, intoRequests.Error?.Code);
    }
}
