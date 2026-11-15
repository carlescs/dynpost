using Dynpost.Application.Common;
using Dynpost.Domain.Runs;

namespace Dynpost.Application.Runs;

/// <summary>A metric of the baseline run next to the same metric of the compared run.</summary>
/// <param name="Metric">
/// samples, errorPercent, average, median, p90, p95, p99, max or throughput; times are in
/// milliseconds, throughput in requests per second and the error rate in percent.
/// </param>
/// <param name="Change">The compared value minus the baseline (percentage points for errorPercent).</param>
/// <param name="ChangePercent">The change relative to the baseline; <c>null</c> when the baseline is 0 and the compared value isn't.</param>
/// <param name="Verdict">better, worse or same (within the noise tolerance, or a metric that is neither better nor worse).</param>
public sealed record MetricDeltaDto(string Metric, double Baseline, double Current, double Change, double? ChangePercent, string Verdict);

/// <summary>A row of a comparison: the whole run, one request, or one environment.</summary>
/// <param name="Key">The request's or environment's workspace path; empty for the total.</param>
/// <param name="Name">What the row shows: <c>Total</c>, the request path or the environment's name.</param>
/// <param name="Metrics">The deltas; empty unless the row is in both runs.</param>
public sealed record ComparisonRowDto(string Key, string Name, bool InBaseline, bool InCurrent, IReadOnlyList<MetricDeltaDto> Metrics);

/// <summary>How a run compares with a baseline run.</summary>
/// <param name="Requests">Per request: the compared run's requests in order, then those only the baseline sent.</param>
/// <param name="Environments">Per environment, the same way; empty when neither run lists environments.</param>
public sealed record RunComparisonDto(
    RunSummaryDto Baseline,
    RunSummaryDto Current,
    ComparisonRowDto Total,
    IReadOnlyList<ComparisonRowDto> Requests,
    IReadOnlyList<ComparisonRowDto> Environments);

/// <summary>
/// Compares a finished run with a baseline (usually an earlier run of the same plan): per-request
/// and overall deltas of the latency percentiles, throughput and error rate.
/// </summary>
public sealed class CompareRuns(RunRegistry registry, GetRunProgress progress)
{
    private static readonly (string Name, Func<StatsDto, double> Value, MetricDirection Direction, MetricTolerance Tolerance)[] Metrics =
    [
        ("samples", s => s.Samples, MetricDirection.Neutral, MetricTolerance.Exact),
        ("errorPercent", s => s.ErrorPercent, MetricDirection.LowerIsBetter, MetricTolerance.ErrorRate),
        ("average", s => s.Average, MetricDirection.LowerIsBetter, MetricTolerance.Latency),
        ("median", s => s.Median, MetricDirection.LowerIsBetter, MetricTolerance.Latency),
        ("p90", s => s.P90, MetricDirection.LowerIsBetter, MetricTolerance.Latency),
        ("p95", s => s.P95, MetricDirection.LowerIsBetter, MetricTolerance.Latency),
        ("p99", s => s.P99, MetricDirection.LowerIsBetter, MetricTolerance.Latency),
        ("max", s => s.Max, MetricDirection.LowerIsBetter, MetricTolerance.Latency),
        ("throughput", s => s.Throughput, MetricDirection.HigherIsBetter, MetricTolerance.Throughput),
    ];

    public async Task<Result<RunComparisonDto>> ExecuteAsync(string baselineRunId, string runId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baselineRunId) || string.IsNullOrWhiteSpace(runId))
        {
            return new Error(ErrorCodes.InvalidRequest, "Choose the two runs to compare.");
        }

        if (baselineRunId == runId)
        {
            return new Error(ErrorCodes.InvalidRequest, "Choose two different runs to compare.");
        }

        foreach (var id in new[] { baselineRunId, runId })
        {
            if (registry.Find(id) is { State: "running" })
            {
                return new Error(ErrorCodes.InvalidRequest, "Wait for the run to finish before comparing it.");
            }
        }

        var baseline = await progress.ExecuteAsync(baselineRunId, cancellationToken);
        if (!baseline.IsSuccess)
        {
            return baseline.Error!;
        }

        var current = await progress.ExecuteAsync(runId, cancellationToken);
        if (!current.IsSuccess)
        {
            return current.Error!;
        }

        return Compare(baseline.Value, current.Value);
    }

    public static RunComparisonDto Compare(RunResultDto baseline, RunResultDto current) => new(
        RunSummaryDto.From(baseline),
        RunSummaryDto.From(current),
        Row(string.Empty, "Total", baseline.Total, current.Total),
        Rows(
            baseline.Requests.Select(r => (r.Request, r.Request, r.Stats)),
            current.Requests.Select(r => (r.Request, r.Request, r.Stats))),
        Rows(
            (baseline.Environments ?? []).Select(e => (e.Environment, e.Name, e.Stats)),
            (current.Environments ?? []).Select(e => (e.Environment, e.Name, e.Stats))));

    private static List<ComparisonRowDto> Rows(
        IEnumerable<(string Key, string Name, StatsDto Stats)> baseline,
        IEnumerable<(string Key, string Name, StatsDto Stats)> current)
    {
        var before = new Dictionary<string, (string Name, StatsDto Stats)>(StringComparer.Ordinal);
        foreach (var (key, name, stats) in baseline)
        {
            before.TryAdd(key, (name, stats));
        }

        var rows = new List<ComparisonRowDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, name, stats) in current)
        {
            if (seen.Add(key))
            {
                rows.Add(Row(key, name, before.TryGetValue(key, out var match) ? match.Stats : null, stats));
            }
        }

        rows.AddRange(before.Where(pair => !seen.Contains(pair.Key)).Select(pair => Row(pair.Key, pair.Value.Name, pair.Value.Stats, null)));
        return rows;
    }

    private static ComparisonRowDto Row(string key, string name, StatsDto? baseline, StatsDto? current) => new(
        key,
        name,
        baseline is not null,
        current is not null,
        baseline is null || current is null
            ? []
            : Metrics.Select(m => Delta(m.Name, RunComparison.Compare(m.Value(baseline), m.Value(current), m.Direction, m.Tolerance))).ToList());

    private static MetricDeltaDto Delta(string metric, MetricDelta delta) => new(
        metric,
        delta.Baseline,
        delta.Current,
        delta.Change,
        delta.ChangePercent,
        delta.Verdict.ToString().ToLowerInvariant());
}
