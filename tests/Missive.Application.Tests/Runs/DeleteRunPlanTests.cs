using Missive.Application.Common;
using Missive.Application.Runs;
using Missive.Application.Sharing;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests.Runs;

public class DeleteRunPlanTests
{
    private const string Root = "/work/api";

    private readonly IRunPlanStore _store = Substitute.For<IRunPlanStore>();
    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly RunRegistry _registry = new();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
    private readonly CurrentWorkspace _current = new();

    public DeleteRunPlanTests()
    {
        _current.Open(Root);
        _store.Delete(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _runs.DeletePlanAsync(Root, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
    }

    private DeleteRunPlan Create() => new(_store, _runs, _registry, _localFiles, _current);

    [Fact]
    public async Task DeletesThePlanAndItsRuns()
    {
        var path = WorkspacePath.Parse("perf/smoke.run.yaml");
        _store.Exists(Root, path).Returns(true);

        var deleted = await Create().ExecuteAsync("perf/smoke.run.yaml");

        Assert.True(deleted.IsSuccess);
        _store.Received().Delete(Root, path);
        await _runs.Received().DeletePlanAsync(Root, "perf/smoke.run.yaml", Arg.Any<CancellationToken>());
        _localFiles.Received().Remove(Root, path);
    }

    [Fact]
    public async Task RefusesPathsThatAreNotRunPlans()
    {
        var deleted = await Create().ExecuteAsync("perf/request.yaml");

        Assert.Equal(ErrorCodes.InvalidPath, deleted.Error!.Code);
        _store.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<WorkspacePath>());
    }

    [Fact]
    public async Task ReportsAMissingPlan()
    {
        var deleted = await Create().ExecuteAsync("gone.run.yaml");

        Assert.Equal(ErrorCodes.NotFound, deleted.Error!.Code);
        _store.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<WorkspacePath>());
    }

    [Fact]
    public async Task RefusesAPlanThatIsBeingRun()
    {
        var path = WorkspacePath.Parse("smoke.run.yaml");
        _store.Exists(Root, path).Returns(true);
        using var reservation = _registry.TryReserve(Root, "smoke.run.yaml");

        var deleted = await Create().ExecuteAsync("smoke.run.yaml");

        Assert.Equal(ErrorCodes.InvalidRequest, deleted.Error!.Code);
        _store.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<WorkspacePath>());
        await _runs.DidNotReceive().DeletePlanAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsTheRunsWhenTheFileCannotBeDeleted()
    {
        var path = WorkspacePath.Parse("smoke.run.yaml");
        _store.Exists(Root, path).Returns(true);
        _store.Delete(Root, path).Returns(new Error(ErrorCodes.Io, "locked"));

        var deleted = await Create().ExecuteAsync("smoke.run.yaml");

        Assert.Equal(ErrorCodes.Io, deleted.Error!.Code);
        _localFiles.DidNotReceive().Remove(Arg.Any<string>(), Arg.Any<WorkspacePath>());
        await _runs.DidNotReceive().DeletePlanAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
