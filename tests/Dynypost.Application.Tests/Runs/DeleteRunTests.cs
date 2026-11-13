using Dynypost.Application.Common;
using Dynypost.Application.Runs;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Runs;
using Dynypost.Domain.Workspaces;
using NSubstitute;

namespace Dynypost.Application.Tests.Runs;

public class DeleteRunTests
{
    private const string Root = "/work/api";
    private const string PlanFile = "plans/perf/smoke.run.yaml";

    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly RunRegistry _registry = new();
    private readonly CurrentWorkspace _current = new();

    public DeleteRunTests()
    {
        _current.Open(Root);
        _runs.DeleteAsync(Root, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _runs.DeletePlanAsync(Root, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
    }

    private static ActiveRun Run(string runId, string planPath = PlanFile)
    {
        var plan = new RunPlan(
            "Smoke",
            [WorkspacePath.Parse("users.yaml")],
            new RunLoad(1, 1, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null),
            null,
            new Dictionary<string, RunAssertion>(),
            RunThresholds.None);
        return new ActiveRun(runId, Root, planPath, plan, DateTimeOffset.UnixEpoch);
    }

    private static ActiveRun Finished(string runId, string planPath = PlanFile)
    {
        var run = Run(runId, planPath);
        run.Finish("completed");
        run.CompletePersistence(null);
        return run;
    }

    [Fact]
    public async Task DeletesASavedRunAndForgetsIt()
    {
        _registry.Add(Finished("run-1"));

        var deleted = await new DeleteRun(_registry, _runs, _current).ExecuteAsync("run-1");

        Assert.True(deleted.IsSuccess);
        await _runs.Received().DeleteAsync(Root, "run-1", Arg.Any<CancellationToken>());
        Assert.Null(_registry.Find("run-1"));
    }

    [Fact]
    public async Task RefusesARunInProgress()
    {
        _registry.Add(Run("run-1"));

        var deleted = await new DeleteRun(_registry, _runs, _current).ExecuteAsync("run-1");

        Assert.Equal(ErrorCodes.InvalidRequest, deleted.Error!.Code);
        await _runs.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.NotNull(_registry.Find("run-1"));
    }

    [Fact]
    public async Task ReportsAMissingRun()
    {
        _runs.DeleteAsync(Root, "gone", Arg.Any<CancellationToken>())
            .Returns(new Error(RunErrorCodes.RunNotFound, "There is no run 'gone'."));

        var deleted = await new DeleteRun(_registry, _runs, _current).ExecuteAsync("gone");

        Assert.Equal(RunErrorCodes.RunNotFound, deleted.Error!.Code);
    }

    [Fact]
    public async Task DeletesSeveralRuns()
    {
        _registry.Add(Finished("run-1"));
        _registry.Add(Finished("run-2"));

        var deleted = await new DeleteRun(_registry, _runs, _current).ExecuteAsync(["run-1", "run-2", "run-1"]);

        Assert.True(deleted.IsSuccess);
        await _runs.Received(1).DeleteAsync(Root, "run-1", Arg.Any<CancellationToken>());
        await _runs.Received(1).DeleteAsync(Root, "run-2", Arg.Any<CancellationToken>());
        Assert.Null(_registry.Find("run-1"));
        Assert.Null(_registry.Find("run-2"));
    }

    [Fact]
    public async Task DeletesNoneOfSeveralRunsWhenOneIsInProgress()
    {
        _registry.Add(Finished("run-1"));
        _registry.Add(Run("run-2"));

        var deleted = await new DeleteRun(_registry, _runs, _current).ExecuteAsync(["run-1", "run-2"]);

        Assert.Equal(ErrorCodes.InvalidRequest, deleted.Error!.Code);
        await _runs.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.NotNull(_registry.Find("run-1"));
    }

    [Fact]
    public async Task DeletesTheOtherRunsWhenOneIsMissing()
    {
        _runs.DeleteAsync(Root, "gone", Arg.Any<CancellationToken>())
            .Returns(new Error(RunErrorCodes.RunNotFound, "There is no run 'gone'."));

        var deleted = await new DeleteRun(_registry, _runs, _current).ExecuteAsync(["gone", "run-2"]);

        Assert.Equal(RunErrorCodes.RunNotFound, deleted.Error!.Code);
        await _runs.Received().DeleteAsync(Root, "run-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesAnEmptySelection()
    {
        var deleted = await new DeleteRun(_registry, _runs, _current).ExecuteAsync(Array.Empty<string>());

        Assert.Equal(ErrorCodes.InvalidRequest, deleted.Error!.Code);
    }

    [Fact]
    public async Task ClearsTheRunsOfAPlan()
    {
        _registry.Add(Finished("run-1"));
        _registry.Add(Finished("run-2", "plans/other.run.yaml"));

        var cleared = await new ClearRuns(_registry, _runs, _current).ExecuteAsync(PlanFile);

        Assert.True(cleared.IsSuccess);
        await _runs.Received().DeletePlanAsync(Root, PlanFile, Arg.Any<CancellationToken>());
        Assert.Null(_registry.Find("run-1"));
        Assert.NotNull(_registry.Find("run-2"));
    }

    [Fact]
    public async Task RefusesToClearAPlanThatIsBeingRun()
    {
        _registry.Add(Run("run-1"));

        var cleared = await new ClearRuns(_registry, _runs, _current).ExecuteAsync(PlanFile);

        Assert.Equal(ErrorCodes.InvalidRequest, cleared.Error!.Code);
        await _runs.DidNotReceive().DeletePlanAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToClearPathsThatAreNotRunPlans()
    {
        var cleared = await new ClearRuns(_registry, _runs, _current).ExecuteAsync("perf/request.yaml");

        Assert.Equal(ErrorCodes.InvalidPath, cleared.Error!.Code);
    }
}
