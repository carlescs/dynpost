using Missive.Application.Common;
using Missive.Application.Folders;
using Missive.Application.Runs;
using Missive.Application.Sharing;
using Missive.Application.Workspaces;
using Missive.Domain.Requests;
using Missive.Domain.Runs;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests.Runs;

public class RunFolderUseCaseTests
{
    private const string Root = "/work/api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IRunPlanStore _store = Substitute.For<IRunPlanStore>();
    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
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
    public async Task List_ShowsFoldersWithPlansOrWithoutRequests()
    {
        var request = Result<HttpRequestDefinition>.Success(new HttpRequestDefinition("Get", RequestMethod.Get, "https://x"));
        _repository.ListAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<WorkspaceEntry>>.Success(
        [
            new FolderEntry(P("empty"), []),
            new FolderEntry(P("perf"), [new FolderEntry(P("perf/nightly"), [])]),
            new FolderEntry(P("shop"),
            [
                new RequestFileEntry(P("shop/list.yaml"), request),
                new FolderEntry(P("shop/load"), []),
            ]),
            new FolderEntry(P("users"), [new RequestFileEntry(P("users/get.yaml"), request)]),
        ]));
        _store.List(Root).Returns(Result<IReadOnlyList<WorkspacePath>>.Success([P("shop/smoke.run.yaml")]));

        var folders = await new ListRunFolders(_repository, _store, _current).ExecuteAsync();

        Assert.Equal(["empty", "perf", "perf/nightly", "shop", "shop/load"], folders.Value);
    }

    [Fact]
    public async Task Rename_MovesTheFolderAndTheRunsOfItsPlans()
    {
        Folder("perf", "perf/smoke.run.yaml", "perf/deep/soak.run.yaml", "perf/_folder.yaml");

        var renamed = await new RenameRunFolder(new RenameFolder(_repository, _localFiles, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("perf", "load");

        Assert.Equal("load", renamed.Value.Path);
        _repository.Received(1).MoveFolder(Root, P("perf"), P("load"));
        await _runs.Received(1).MovePlanAsync(Root, "perf/smoke.run.yaml", "load/smoke.run.yaml", Arg.Any<CancellationToken>());
        await _runs.Received(1).MovePlanAsync(Root, "perf/deep/soak.run.yaml", "load/deep/soak.run.yaml", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_RefusesFoldersWithRequests()
    {
        Folder("shop", "shop/smoke.run.yaml", "shop/list.yaml");

        var renamed = await new RenameRunFolder(new RenameFolder(_repository, _localFiles, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("shop", "store");

        Assert.Equal(ErrorCodes.InvalidRequest, renamed.Error?.Code);
        Assert.Contains("Explorer", renamed.Error!.Message);
        _repository.DidNotReceiveWithAnyArgs().MoveFolder(default!, default!, default!);
    }

    [Fact]
    public async Task Move_RefusesFoldersWithARunningPlan()
    {
        Folder("perf", "perf/smoke.run.yaml");
        Running("perf/smoke.run.yaml");

        var moved = await new MoveRunFolder(new MoveFolder(_repository, _localFiles, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("perf", "");

        Assert.Equal(ErrorCodes.InvalidRequest, moved.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().MoveFolder(default!, default!, default!);
    }

    [Fact]
    public async Task Move_MovesTheFolderAndTheRunsOfItsPlans()
    {
        Folder("perf", "perf/smoke.run.yaml");
        Folder("archive");

        var moved = await new MoveRunFolder(new MoveFolder(_repository, _localFiles, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("perf", "archive");

        Assert.Equal("archive/perf", moved.Value.Path);
        await _runs.Received(1).MovePlanAsync(Root, "perf/smoke.run.yaml", "archive/perf/smoke.run.yaml", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Duplicate_CopiesAFolderOfPlans()
    {
        Folder("perf", "perf/smoke.run.yaml");

        var copy = new DuplicateRunFolder(new DuplicateFolder(_repository, _localFiles, _current), _repository, _current)
            .Execute("perf");

        Assert.Equal("perf copy", copy.Value.Path);
        _repository.Received(1).CopyFile(Root, P("perf/smoke.run.yaml"), P("perf copy/smoke.run.yaml"));
    }

    [Fact]
    public void Duplicate_RefusesFoldersWithRequests()
    {
        Folder("shop", "shop/list.yaml");

        var copy = new DuplicateRunFolder(new DuplicateFolder(_repository, _localFiles, _current), _repository, _current)
            .Execute("shop");

        Assert.Equal(ErrorCodes.InvalidRequest, copy.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().CopyFile(default!, default!, default!);
    }

    [Fact]
    public async Task Delete_DeletesTheFolderAndTheRunsOfItsPlans()
    {
        Folder("perf", "perf/smoke.run.yaml", "perf/deep/soak.run.yaml");

        var deleted = await new DeleteRunFolder(new DeleteFolder(_repository, _localFiles, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("perf");

        Assert.True(deleted.IsSuccess);
        _repository.Received(1).DeleteFolder(Root, P("perf"));
        await _runs.Received(1).DeletePlanAsync(Root, "perf/smoke.run.yaml", Arg.Any<CancellationToken>());
        await _runs.Received(1).DeletePlanAsync(Root, "perf/deep/soak.run.yaml", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_RefusesFoldersWithRequests()
    {
        Folder("shop", "shop/list.yaml", "shop/smoke.run.yaml");

        var deleted = await new DeleteRunFolder(new DeleteFolder(_repository, _localFiles, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("shop");

        Assert.Equal(ErrorCodes.InvalidRequest, deleted.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().DeleteFolder(default!, default!);
        await _runs.DidNotReceiveWithAnyArgs().DeletePlanAsync(default!, default!, default);
    }

    [Fact]
    public async Task Delete_KeepsTheRunsWhenTheFolderCantBeDeleted()
    {
        Folder("perf", "perf/smoke.run.yaml", "perf/notes.txt");

        var deleted = await new DeleteRunFolder(new DeleteFolder(_repository, _localFiles, _current), _repository, _runs, _registry, _current)
            .ExecuteAsync("perf");

        Assert.Equal(ErrorCodes.FolderNotEmpty, deleted.Error?.Code);
        await _runs.DidNotReceiveWithAnyArgs().DeletePlanAsync(default!, default!, default);
    }

    private MoveRunPlan MovePlan() => new(_store, _repository, _runs, _registry, _localFiles, _current);

    [Fact]
    public async Task MovePlan_MovesTheFileWithItsRunsAndSharing()
    {
        _store.Exists(Root, P("smoke.run.yaml")).Returns(true);
        _repository.DirectoryExists(Root, P("perf")).Returns(true);

        var moved = await MovePlan().ExecuteAsync("smoke.run.yaml", "perf");

        Assert.Equal("perf/smoke.run.yaml", moved.Value.Path);
        _store.Received(1).Move(Root, P("smoke.run.yaml"), P("perf/smoke.run.yaml"));
        _localFiles.Received(1).Move(Root, P("smoke.run.yaml"), P("perf/smoke.run.yaml"));
        await _runs.Received(1).MovePlanAsync(Root, "smoke.run.yaml", "perf/smoke.run.yaml", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MovePlan_ToTheRoot()
    {
        _store.Exists(Root, P("perf/smoke.run.yaml")).Returns(true);

        var moved = await MovePlan().ExecuteAsync("perf/smoke.run.yaml", "");

        Assert.Equal("smoke.run.yaml", moved.Value.Path);
    }

    [Fact]
    public async Task MovePlan_LeavesAPlanInItsOwnFolderAlone()
    {
        _store.Exists(Root, P("perf/smoke.run.yaml")).Returns(true);
        _repository.DirectoryExists(Root, P("perf")).Returns(true);

        var moved = await MovePlan().ExecuteAsync("perf/smoke.run.yaml", "perf");

        Assert.Equal("perf/smoke.run.yaml", moved.Value.Path);
        _store.DidNotReceiveWithAnyArgs().Move(default!, default!, default!);
    }

    [Fact]
    public async Task MovePlan_RefusesTakenNames()
    {
        _store.Exists(Root, P("smoke.run.yaml")).Returns(true);
        _store.Exists(Root, P("perf/smoke.run.yaml")).Returns(true);
        _repository.DirectoryExists(Root, P("perf")).Returns(true);

        var moved = await MovePlan().ExecuteAsync("smoke.run.yaml", "perf");

        Assert.Equal(ErrorCodes.AlreadyExists, moved.Error?.Code);
        _store.DidNotReceiveWithAnyArgs().Move(default!, default!, default!);
    }

    [Fact]
    public async Task MovePlan_RefusesARunningPlan()
    {
        _store.Exists(Root, P("smoke.run.yaml")).Returns(true);
        _repository.DirectoryExists(Root, P("perf")).Returns(true);
        Running("smoke.run.yaml");

        var moved = await MovePlan().ExecuteAsync("smoke.run.yaml", "perf");

        Assert.Equal(ErrorCodes.InvalidRequest, moved.Error?.Code);
        _store.DidNotReceiveWithAnyArgs().Move(default!, default!, default!);
    }

    [Fact]
    public async Task MovePlan_RefusesPathsThatAreNotPlans()
    {
        var moved = await MovePlan().ExecuteAsync("users/get.yaml", "");

        Assert.Equal(ErrorCodes.InvalidPath, moved.Error?.Code);
    }
}
