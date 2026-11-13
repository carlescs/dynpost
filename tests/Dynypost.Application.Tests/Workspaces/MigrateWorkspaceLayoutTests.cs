using Dynypost.Application.Common;
using Dynypost.Application.History;
using Dynypost.Application.Runs;
using Dynypost.Application.Sharing;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Workspaces;
using NSubstitute;

namespace Dynypost.Application.Tests.Workspaces;

public class MigrateWorkspaceLayoutTests
{
    private const string Root = "/work/api";

    private readonly IWorkspaceLayoutStore _layout = Substitute.For<IWorkspaceLayoutStore>();
    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IRunPlanStore _plans = Substitute.For<IRunPlanStore>();
    private readonly IRunPlanFormat _format = Substitute.For<IRunPlanFormat>();
    private readonly IHistoryStore _history = Substitute.For<IHistoryStore>();
    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();

    public MigrateWorkspaceLayoutTests()
    {
        _layout.ReadVersionAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<int>.Success(1));
        _layout.WriteVersionAsync(Root, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _repository.CreateFolder(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.MoveFile(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.ReadFolderContents(Root, Arg.Any<WorkspacePath>()).Returns(new FolderContents([], [], []));
        _plans.List(Root).Returns(Result<IReadOnlyList<WorkspacePath>>.Success([]));
    }

    private MigrateWorkspaceLayout UseCase() => new(_layout, _repository, _plans, _format, _history, _runs, _localFiles);

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);

    private void Scan(string[] requests, string[]? plans = null, string[]? empty = null) =>
        _layout.ScanLegacyAsync(Root, Arg.Any<CancellationToken>()).Returns(new LegacyLayoutScan(
            requests.Select(P).ToList(), (plans ?? []).Select(P).ToList(), (empty ?? []).Select(P).ToList()));

    [Fact]
    public async Task DoesNothingForAWorkspaceInTheCurrentLayout()
    {
        _layout.ReadVersionAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<int>.Success(2));

        Assert.Null(await UseCase().ExecuteAsync(Root));

        await _layout.DidNotReceiveWithAnyArgs().ScanLegacyAsync(default!, default);
        await _layout.DidNotReceiveWithAnyArgs().WriteVersionAsync(default!, default, default);
    }

    [Fact]
    public async Task RenamesTheFilesOfTheFormerNameBeforeReadingTheLayout()
    {
        _layout.ReadVersionAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<int>.Success(2));

        await UseCase().ExecuteAsync(Root);

        Received.InOrder(() =>
        {
            _layout.RenameLegacyFilesAsync(Root, Arg.Any<CancellationToken>());
            _layout.ReadVersionAsync(Root, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RecordsTheLayoutWhenThereIsNothingToMove()
    {
        Scan([], empty: ["src"]);

        Assert.Null(await UseCase().ExecuteAsync(Root));

        await _layout.Received(1).WriteVersionAsync(Root, WorkspaceLayout.CurrentLayoutVersion, Arg.Any<CancellationToken>());
        _repository.DidNotReceiveWithAnyArgs().MoveFile(default!, default!, default!);
        _repository.DidNotReceiveWithAnyArgs().CreateFolder(default!, default!);
    }

    [Fact]
    public async Task MovesFilesWithTheirHistoryRunsAndSharing_AndRewritesPlans()
    {
        Scan(["users/get.yaml", "users/_folder.yaml"], ["perf/smoke.run.yaml", "requests/load.run.yaml"]);
        _plans.List(Root).Returns(Result<IReadOnlyList<WorkspacePath>>.Success([P("plans/perf/smoke.run.yaml")]));
        _plans.ReadAsync(Root, P("plans/perf/smoke.run.yaml"), Arg.Any<CancellationToken>()).Returns(Result<string>.Success("old"));
        _format.RewriteRequestPaths("old", Arg.Any<IReadOnlyDictionary<string, string>>()).Returns("new");
        _plans.WriteAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var migration = await UseCase().ExecuteAsync(Root);

        Assert.Equal(new LayoutMigrationDto(2, 2, []), migration with { LeftInPlace = [] });
        _repository.Received(1).MoveFile(Root, P("users/get.yaml"), P("requests/users/get.yaml"));
        _repository.Received(1).MoveFile(Root, P("users/_folder.yaml"), P("requests/users/_folder.yaml"));
        _repository.Received(1).MoveFile(Root, P("perf/smoke.run.yaml"), P("plans/perf/smoke.run.yaml"));
        _repository.Received(1).MoveFile(Root, P("requests/load.run.yaml"), P("plans/load.run.yaml"));
        _history.Received(1).Move(Root, P("users/get.yaml"), P("requests/users/get.yaml"));
        _history.DidNotReceive().Move(Root, P("users/_folder.yaml"), Arg.Any<WorkspacePath>());
        _localFiles.Received(1).Move(Root, P("users/get.yaml"), P("requests/users/get.yaml"));
        _localFiles.Received(1).Move(Root, P("perf/smoke.run.yaml"), P("plans/perf/smoke.run.yaml"));
        await _runs.Received(1).MovePlanAsync(Root, "perf/smoke.run.yaml", "plans/perf/smoke.run.yaml", Arg.Any<CancellationToken>());
        _format.Received(1).RewriteRequestPaths("old", Arg.Is<IReadOnlyDictionary<string, string>>(m =>
            m.Count == 1 && m["users/get.yaml"] == "requests/users/get.yaml"));
        await _plans.Received(1).WriteAsync(Root, P("plans/perf/smoke.run.yaml"), "new", Arg.Any<CancellationToken>());
        await _layout.Received(1).WriteVersionAsync(Root, WorkspaceLayout.CurrentLayoutVersion, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeavesFilesWhoseNewPlaceIsTaken()
    {
        Scan(["users/get.yaml"]);
        _repository.EntryExists(Root, P("requests/users/get.yaml")).Returns(true);

        var migration = await UseCase().ExecuteAsync(Root);

        Assert.Equal(0, migration!.Requests);
        Assert.Equal(["users/get.yaml ('requests/users/get.yaml' already exists)"], migration.LeftInPlace);
        _repository.DidNotReceiveWithAnyArgs().MoveFile(default!, default!, default!);
        _history.DidNotReceiveWithAnyArgs().Move(default!, default!, default!);
    }

    [Fact]
    public async Task RemovesEmptiedFolders_AndRecreatesEmptyCollections()
    {
        Scan(["users/get.yaml"], empty: ["drafts"]);
        _repository.DirectoryExists(Root, P("users")).Returns(true);
        _repository.DirectoryExists(Root, P("drafts")).Returns(true);
        _repository.FileExists(Root, P("drafts/.gitkeep")).Returns(true);
        _repository.ReadFolderContents(Root, P("drafts")).Returns(new FolderContents([P("drafts/.gitkeep")], [], []));
        _repository.DirectoryExists(Root, P("docs")).Returns(true);

        await UseCase().ExecuteAsync(Root);

        _repository.Received(1).DeleteFolder(Root, P("users"));
        _repository.Received(1).CreateFolder(Root, P("requests/drafts"));
        _repository.Received(1).MoveFile(Root, P("drafts/.gitkeep"), P("requests/drafts/.gitkeep"));
        _repository.Received(1).DeleteFolder(Root, P("drafts"));
    }

    [Fact]
    public async Task KeepsFoldersThatStillHoldOtherFiles()
    {
        Scan(["users/get.yaml"]);
        _repository.DirectoryExists(Root, P("users")).Returns(true);
        _repository.ReadFolderContents(Root, P("users")).Returns(new FolderContents([P("users/README.md")], [], []));

        await UseCase().ExecuteAsync(Root);

        _repository.DidNotReceiveWithAnyArgs().DeleteFolder(default!, default!);
    }
}
