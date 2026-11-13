using Missive.Application.Common;
using Missive.Application.Runs;
using Missive.Application.Workspaces;
using Missive.Domain.Runs;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests.Runs;

public class WaitForRunTests
{
    private const string Root = "/work/api";

    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly RunRegistry _registry = new();
    private readonly CurrentWorkspace _current = new();
    private readonly WaitForRun _wait;

    public WaitForRunTests()
    {
        _current.Open(Root);
        _wait = new WaitForRun(_registry, new GetRunProgress(_registry, _runs, _current));
    }

    private ActiveRun Start()
    {
        var plan = new RunPlan(
            "Smoke",
            [WorkspacePath.Parse("requests/users.yaml")],
            new RunLoad(1, 1, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null),
            null,
            new Dictionary<string, RunAssertion>(),
            RunThresholds.None);
        var run = new ActiveRun("run-1", Root, "plans/smoke.run.yaml", plan, DateTimeOffset.UnixEpoch);
        _registry.Add(run);
        return run;
    }

    [Fact]
    public async Task ARunThatFinishesWhileWaiting_IsReportedFinished()
    {
        var run = Start();

        var waiting = _wait.ExecuteAsync("run-1", TimeSpan.FromSeconds(10));
        Assert.False(waiting.IsCompleted);
        run.Finish("completed");
        run.CompletePersistence(null);
        var result = await waiting;

        Assert.Equal("completed", result.Value.State);
    }

    [Fact]
    public async Task ARunStillGoingWhenTheWaitEnds_IsReportedRunning()
    {
        Start();

        var result = await _wait.ExecuteAsync("run-1", TimeSpan.FromMilliseconds(50));

        Assert.Equal("running", result.Value.State);
    }

    [Fact]
    public async Task WithoutAWait_TheRunIsReportedAtOnce()
    {
        Start();

        var result = await _wait.ExecuteAsync("run-1", TimeSpan.Zero);

        Assert.Equal("running", result.Value.State);
    }

    [Fact]
    public async Task ARunNoLongerInProgress_IsReadFromTheStore()
    {
        _runs.GetAsync(Root, "old", Arg.Any<CancellationToken>())
            .Returns(Result<RunResultDto>.Failure(RunErrorCodes.RunNotFound, "gone"));

        var result = await _wait.ExecuteAsync("old", TimeSpan.FromSeconds(10));

        Assert.Equal(RunErrorCodes.RunNotFound, result.Error!.Code);
    }
}
