using Dynpost.Application.Common;
using Dynpost.Application.Runs;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Runs;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests.Runs;

public class CompareRunsTests
{
    private const string Root = "/work/api";
    private const string PlanFile = "plans/perf/smoke.run.yaml";

    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly RunRegistry _registry = new();
    private readonly CurrentWorkspace _current = new();
    private readonly CompareRuns _compare;

    public CompareRunsTests()
    {
        _current.Open(Root);
        _runs.GetAsync(Root, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Error(RunErrorCodes.RunNotFound, "There is no such run."));
        _compare = new CompareRuns(_registry, new GetRunProgress(_registry, _runs, _current));
    }

    private static StatsDto Stats(long samples, long errors, double p95, double throughput) => new(
        samples, errors, samples == 0 ? 0 : 100.0 * errors / samples,
        1, p95 / 2, p95 / 2, p95 * 0.9, p95, p95 * 1.1, p95 * 2, throughput, 0, 0,
        new Dictionary<string, long>(), new Dictionary<string, long>());

    private static RunResultDto Run(
        string runId,
        StatsDto total,
        IReadOnlyList<RequestStatsDto> requests,
        IReadOnlyList<EnvironmentStatsDto>? environments = null) => new(
        runId, PlanFile, "Smoke", "completed", DateTimeOffset.UnixEpoch, 10_000, 2, 0,
        total, requests, [], [], null, [], [], null, environments);

    private void Save(RunResultDto run) =>
        _runs.GetAsync(Root, run.RunId, Arg.Any<CancellationToken>()).Returns(run);

    [Fact]
    public async Task ComparesTheTotalsAndEachRequest()
    {
        Save(Run("old", Stats(100, 0, 200, 10), [
            new("requests/users.yaml", Stats(50, 0, 200, 5)),
            new("requests/gone.yaml", Stats(50, 0, 100, 5)),
        ]));
        Save(Run("new", Stats(100, 5, 100, 20), [
            new("requests/added.yaml", Stats(50, 5, 50, 10)),
            new("requests/users.yaml", Stats(50, 0, 300, 5)),
        ]));

        var result = await _compare.ExecuteAsync("old", "new");

        Assert.True(result.IsSuccess);
        var comparison = result.Value;
        Assert.Equal("old", comparison.Baseline.RunId);
        Assert.Equal("new", comparison.Current.RunId);

        var total = comparison.Total.Metrics.ToDictionary(m => m.Metric);
        Assert.Equal("better", total["p95"].Verdict);
        Assert.Equal(-50, total["p95"].ChangePercent);
        Assert.Equal("better", total["throughput"].Verdict);
        Assert.Equal("worse", total["errorPercent"].Verdict);
        Assert.Equal(5, total["errorPercent"].Change);
        Assert.Equal("same", total["samples"].Verdict);

        Assert.Equal(["requests/added.yaml", "requests/users.yaml", "requests/gone.yaml"], comparison.Requests.Select(r => r.Key));
        var added = comparison.Requests[0];
        Assert.False(added.InBaseline);
        Assert.True(added.InCurrent);
        Assert.Empty(added.Metrics);
        Assert.Equal("worse", comparison.Requests[1].Metrics.Single(m => m.Metric == "p95").Verdict);
        Assert.False(comparison.Requests[2].InCurrent);
        Assert.Empty(comparison.Environments);
    }

    [Fact]
    public async Task ComparesEnvironmentsByPath()
    {
        Save(Run("old", Stats(10, 0, 100, 1), [], [new("environments/dev.yaml", "dev", Stats(10, 0, 100, 1))]));
        Save(Run("new", Stats(10, 0, 100, 1), [], [new("environments/dev.yaml", "Dev", Stats(10, 0, 100, 1))]));

        var result = await _compare.ExecuteAsync("old", "new");

        var row = Assert.Single(result.Value.Environments);
        Assert.Equal("Dev", row.Name);
        Assert.All(row.Metrics, m => Assert.Equal("same", m.Verdict));
    }

    [Fact]
    public async Task AMissingRun_IsReported()
    {
        Save(Run("old", Stats(10, 0, 100, 1), []));

        var result = await _compare.ExecuteAsync("old", "missing");

        Assert.Equal(RunErrorCodes.RunNotFound, result.Error?.Code);
    }

    [Theory]
    [InlineData("run-1", "run-1")]
    [InlineData("", "run-1")]
    public async Task TwoDifferentRuns_AreRequired(string baseline, string run) =>
        Assert.Equal(ErrorCodes.InvalidRequest, (await _compare.ExecuteAsync(baseline, run)).Error?.Code);

    [Fact]
    public async Task ARunningRun_IsRefused()
    {
        var plan = new RunPlan(
            "Smoke",
            [WorkspacePath.Parse("users.yaml")],
            new RunLoad(1, 1, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null),
            null,
            new Dictionary<string, RunAssertion>(),
            RunThresholds.None);
        _registry.Add(new ActiveRun("live", Root, PlanFile, plan, DateTimeOffset.UnixEpoch));
        Save(Run("old", Stats(10, 0, 100, 1), []));

        var result = await _compare.ExecuteAsync("old", "live");

        Assert.Equal(ErrorCodes.InvalidRequest, result.Error?.Code);
    }
}
