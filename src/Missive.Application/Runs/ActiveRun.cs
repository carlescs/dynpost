using System.Diagnostics;
using Missive.Application.Http;
using Missive.Domain.Requests;
using Missive.Domain.Runs;
using Missive.Domain.Workspaces;

namespace Missive.Application.Runs;

/// <summary>The in-memory state of a run while it executes. Safe to read from other threads.</summary>
public sealed class ActiveRun(string runId, string root, string planPath, RunPlan plan, DateTimeOffset startedAt)
{
    public const int MaxFailedSamples = 100;
    public const int MaxSlowestSamples = 50;
    private const int MaxPreviewChars = 2000;

    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly StatsAccumulator _total = new();
    private readonly Dictionary<string, StatsAccumulator> _requests = plan.Requests
        .Select(request => request.Value)
        .Distinct(StringComparer.Ordinal)
        .ToDictionary(path => path, _ => new StatsAccumulator(), StringComparer.Ordinal);
    private readonly Timeline _timeline = new();
    private readonly List<SampleDto> _failed = [];
    private readonly List<SampleDto> _slowest = [];
    private int _activeThreads;
    private TimeSpan? _finishedAt;

    public string RunId { get; } = runId;

    public string Root { get; } = root;

    public string PlanPath { get; } = planPath;

    public RunPlan Plan { get; } = plan;

    public CancellationTokenSource Cancellation { get; } = new();

    public string State { get; private set; } = "running";

    public string? Message { get; private set; }

    public TimeSpan Elapsed => _finishedAt ?? _clock.Elapsed;

    public void ThreadStarted() => Interlocked.Increment(ref _activeThreads);

    public void ThreadEnded() => Interlocked.Decrement(ref _activeThreads);

    public void Add(WorkspacePath request, SampleDto sample, long bytesSent, long bytesReceived)
    {
        var failed = sample.Error is not null;
        _total.Add(sample.LatencyMs, sample.Status, sample.Error, bytesSent, bytesReceived);
        _requests[request.Value].Add(sample.LatencyMs, sample.Status, sample.Error, bytesSent, bytesReceived);
        _timeline.Add(TimeSpan.FromMilliseconds(sample.AtMs), sample.LatencyMs, failed);

        lock (_gate)
        {
            if (failed)
            {
                if (_failed.Count < MaxFailedSamples)
                {
                    _failed.Add(sample);
                }
            }
            else if (_slowest.Count < MaxSlowestSamples || sample.LatencyMs > _slowest[^1].LatencyMs)
            {
                var index = _slowest.FindIndex(existing => existing.LatencyMs < sample.LatencyMs);
                _slowest.Insert(index < 0 ? _slowest.Count : index, sample with { ResponsePreview = null });
                if (_slowest.Count > MaxSlowestSamples)
                {
                    _slowest.RemoveAt(_slowest.Count - 1);
                }
            }
        }
    }

    public TimeSpan Now => _clock.Elapsed;

    public void Finish(string state, string? message = null)
    {
        _finishedAt = _clock.Elapsed;
        Message = message;
        State = state;
    }

    public static string? Preview(byte[] body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        var text = System.Text.Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, MaxPreviewChars * 2));
        return text.Length > MaxPreviewChars ? text[..MaxPreviewChars] + "…" : text;
    }

    public RunResultDto ToDto()
    {
        var elapsed = Elapsed;
        var total = _total.Snapshot(elapsed);
        var thresholds = ThresholdEvaluator.Evaluate(Plan.Thresholds, total)
            .Select(outcome => new ThresholdDto(outcome.Name, outcome.Limit, outcome.Actual, outcome.Passed))
            .ToList();

        List<SampleDto> failed;
        List<SampleDto> slowest;
        lock (_gate)
        {
            failed = [.. _failed];
            slowest = [.. _slowest];
        }

        return new RunResultDto(
            RunId,
            PlanPath,
            Plan.Name,
            State,
            startedAt,
            (long)elapsed.TotalMilliseconds,
            Plan.Load.Threads,
            Volatile.Read(ref _activeThreads),
            ToDto(total),
            _requests.Select(pair => new RequestStatsDto(pair.Key, ToDto(pair.Value.Snapshot(elapsed)))).ToList(),
            _timeline.Points().Select(point => new TimelinePointDto(point.Second, point.Samples, point.Errors, Math.Round(point.AverageMs, 1))).ToList(),
            thresholds,
            State == "completed" ? thresholds.All(t => t.Passed) : null,
            failed,
            slowest,
            Message);
    }

    public static StatsDto ToDto(StatsSnapshot s) => new(
        s.Samples,
        s.Errors,
        Math.Round(s.ErrorPercent, 2),
        Math.Round(s.Min, 1),
        Math.Round(s.Average, 1),
        Math.Round(s.Median, 1),
        Math.Round(s.P90, 1),
        Math.Round(s.P95, 1),
        Math.Round(s.P99, 1),
        Math.Round(s.Max, 1),
        Math.Round(s.ThroughputPerSecond, 2),
        s.BytesSent,
        s.BytesReceived,
        s.StatusCodes,
        s.ErrorReasons);
}
