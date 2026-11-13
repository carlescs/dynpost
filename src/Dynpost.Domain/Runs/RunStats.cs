namespace Dynpost.Domain.Runs;

/// <summary>The aggregate numbers of a set of samples, like JMeter's Aggregate Report row.</summary>
public sealed record StatsSnapshot(
    long Samples,
    long Errors,
    double Min,
    double Average,
    double Median,
    double P90,
    double P95,
    double P99,
    double Max,
    double ThroughputPerSecond,
    long BytesSent,
    long BytesReceived,
    IReadOnlyDictionary<string, long> StatusCodes,
    IReadOnlyDictionary<string, long> ErrorReasons)
{
    public double ErrorPercent => Samples == 0 ? 0 : 100.0 * Errors / Samples;
}

/// <summary>Collects samples from many threads and summarizes them. Thread-safe.</summary>
public sealed class StatsAccumulator
{
    private const int MaxErrorReasons = 50;

    private readonly object _gate = new();
    private readonly LatencyHistogram _histogram = new();
    private readonly Dictionary<string, long> _statusCodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _errorReasons = new(StringComparer.Ordinal);
    private long _samples;
    private long _errors;
    private double _min = double.MaxValue;
    private double _max;
    private double _sum;
    private long _bytesSent;
    private long _bytesReceived;

    /// <param name="statusCode">The HTTP status, or <c>null</c> when there was no response.</param>
    /// <param name="errorReason">Why the sample counts as an error, or <c>null</c> when it succeeded.</param>
    public void Add(double latencyMs, int? statusCode, string? errorReason, long bytesSent, long bytesReceived)
    {
        lock (_gate)
        {
            _samples++;
            _histogram.Record(latencyMs);
            _min = Math.Min(_min, latencyMs);
            _max = Math.Max(_max, latencyMs);
            _sum += latencyMs;
            _bytesSent += bytesSent;
            _bytesReceived += bytesReceived;
            var status = statusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
            _statusCodes[status] = _statusCodes.GetValueOrDefault(status) + 1;
            if (errorReason is not null)
            {
                _errors++;
                var reason = _errorReasons.ContainsKey(errorReason) || _errorReasons.Count < MaxErrorReasons ? errorReason : "other";
                _errorReasons[reason] = _errorReasons.GetValueOrDefault(reason) + 1;
            }
        }
    }

    /// <param name="elapsed">The time the samples were collected over, for the throughput.</param>
    public StatsSnapshot Snapshot(TimeSpan elapsed)
    {
        lock (_gate)
        {
            return new StatsSnapshot(
                _samples,
                _errors,
                _samples == 0 ? 0 : _min,
                _samples == 0 ? 0 : _sum / _samples,
                _histogram.Percentile(50),
                _histogram.Percentile(90),
                _histogram.Percentile(95),
                _histogram.Percentile(99),
                _max,
                elapsed.TotalSeconds > 0 ? _samples / elapsed.TotalSeconds : 0,
                _bytesSent,
                _bytesReceived,
                new Dictionary<string, long>(_statusCodes),
                new Dictionary<string, long>(_errorReasons));
        }
    }
}

/// <summary>One second of the run, for the live chart.</summary>
public sealed record TimelinePoint(int Second, long Samples, long Errors, double AverageMs);

/// <summary>Per-second totals over the run. Thread-safe.</summary>
public sealed class Timeline
{
    private readonly object _gate = new();
    private readonly SortedDictionary<int, (long Samples, long Errors, double Sum)> _seconds = [];

    public void Add(TimeSpan sinceStart, double latencyMs, bool error)
    {
        var second = (int)Math.Max(0, sinceStart.TotalSeconds);
        lock (_gate)
        {
            var (samples, errors, sum) = _seconds.GetValueOrDefault(second);
            _seconds[second] = (samples + 1, errors + (error ? 1 : 0), sum + latencyMs);
        }
    }

    public IReadOnlyList<TimelinePoint> Points()
    {
        lock (_gate)
        {
            return _seconds.Select(pair => new TimelinePoint(pair.Key, pair.Value.Samples, pair.Value.Errors, pair.Value.Sum / pair.Value.Samples)).ToList();
        }
    }
}

public sealed record ThresholdOutcome(string Name, string Limit, string Actual, bool Passed);

public static class ThresholdEvaluator
{
    public static IReadOnlyList<ThresholdOutcome> Evaluate(RunThresholds thresholds, StatsSnapshot total)
    {
        var outcomes = new List<ThresholdOutcome>();

        void Latency(string name, TimeSpan? limit, double actual)
        {
            if (limit is { } value)
            {
                outcomes.Add(new ThresholdOutcome(name, RunText.FormatDuration(value), $"{actual:0.#}ms", actual <= value.TotalMilliseconds));
            }
        }

        Latency("p50", thresholds.P50, total.Median);
        Latency("p90", thresholds.P90, total.P90);
        Latency("p95", thresholds.P95, total.P95);
        Latency("p99", thresholds.P99, total.P99);
        Latency("average", thresholds.Average, total.Average);
        if (thresholds.ErrorRatePercent is { } rate)
        {
            outcomes.Add(new ThresholdOutcome("errorRate", $"{rate:0.##}%", $"{total.ErrorPercent:0.##}%", total.ErrorPercent <= rate));
        }

        if (thresholds.MinThroughput is { } throughput)
        {
            outcomes.Add(new ThresholdOutcome("throughput", $"≥ {throughput:0.##}/s", $"{total.ThroughputPerSecond:0.##}/s", total.ThroughputPerSecond >= throughput));
        }

        return outcomes;
    }
}
