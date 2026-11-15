namespace Dynpost.Domain.Runs;

/// <summary>Which way a metric should move for a run to count as better.</summary>
public enum MetricDirection
{
    /// <summary>Latencies and error rates: less is better.</summary>
    LowerIsBetter,

    /// <summary>Throughput: more is better.</summary>
    HigherIsBetter,

    /// <summary>Counts such as the number of samples, which are neither better nor worse.</summary>
    Neutral,
}

/// <summary>Whether a metric got better, worse or stayed about the same from one run to the next.</summary>
public enum DeltaVerdict
{
    Same,
    Better,
    Worse,
}

/// <summary>
/// How much a metric must change before it counts as a change rather than noise: by more than
/// <paramref name="Absolute"/> (in the metric's unit) and by at least <paramref name="Relative"/>
/// (a fraction of the baseline; 0 to judge by the absolute change alone).
/// </summary>
public sealed record MetricTolerance(double Absolute, double Relative)
{
    /// <summary>Latencies, in milliseconds: half a millisecond and 5%.</summary>
    public static readonly MetricTolerance Latency = new(0.5, 0.05);

    /// <summary>Throughput, in requests per second: 5%.</summary>
    public static readonly MetricTolerance Throughput = new(0.01, 0.05);

    /// <summary>Error rates, in percentage points: a tenth of a point.</summary>
    public static readonly MetricTolerance ErrorRate = new(0.1, 0);

    /// <summary>Any change counts.</summary>
    public static readonly MetricTolerance Exact = new(0, 0);
}

/// <summary>A metric of a baseline run next to the same metric of a later run.</summary>
/// <param name="Change">The current value minus the baseline.</param>
/// <param name="ChangePercent">
/// The change as a percentage of the baseline; <c>null</c> when the baseline is 0 and the current
/// value isn't.
/// </param>
public sealed record MetricDelta(double Baseline, double Current, double Change, double? ChangePercent, DeltaVerdict Verdict);

/// <summary>Compares the numbers of two runs.</summary>
public static class RunComparison
{
    public static MetricDelta Compare(double baseline, double current, MetricDirection direction, MetricTolerance tolerance)
    {
        var change = current - baseline;
        double? percent = baseline == 0 ? (change == 0 ? 0 : null) : 100.0 * change / baseline;
        var significant = Math.Abs(change) > tolerance.Absolute
            && (tolerance.Relative <= 0 || percent is not { } p || Math.Abs(p) >= 100.0 * tolerance.Relative);
        var verdict = !significant || direction == MetricDirection.Neutral
            ? DeltaVerdict.Same
            : (change < 0) == (direction == MetricDirection.LowerIsBetter) ? DeltaVerdict.Better : DeltaVerdict.Worse;
        return new MetricDelta(baseline, current, change, percent, verdict);
    }
}
