using System.Diagnostics;
using Dynpost.Application.Http;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Runs;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Runs;

/// <summary>The in-memory state of a run while it executes. Safe to read from other threads.</summary>
/// <param name="environmentNames">
/// The name of each environment path the run uses, in the plan's order, to keep statistics by
/// environment; <c>null</c> or empty when it uses the selected environment.
/// </param>
public sealed class ActiveRun(
    string runId,
    string root,
    string planPath,
    RunPlan plan,
    DateTimeOffset startedAt,
    IReadOnlyList<KeyValuePair<string, string>>? environmentNames = null)
{
    public const int MaxFailedSamples = 100;
    public const int MaxSlowestSamples = 50;

    /// <summary>How many samples the request log of a run keeps, in the order they finish.</summary>
    public const int MaxLoggedSamples = 10_000;
    private const int MaxPreviewChars = 2000;

    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly StatsAccumulator _total = new();
    private readonly Dictionary<string, StatsAccumulator> _requests = plan.Requests
        .Select(request => request.Value)
        .Distinct(StringComparer.Ordinal)
        .ToDictionary(path => path, _ => new StatsAccumulator(), StringComparer.Ordinal);
    private readonly List<(string Path, string Name, StatsAccumulator Stats)> _environments = (environmentNames ?? [])
        .DistinctBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => (pair.Key, pair.Value, new StatsAccumulator()))
        .ToList();
    private readonly Timeline _timeline = new();
    private readonly List<SampleDto> _failed = [];
    private readonly List<SampleDto> _slowest = [];
    private readonly List<SampleDto> _log = [];
    private int _logged;
    private int _responses;
    private long _sent;
    private readonly TaskCompletionSource _persistence = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activeThreads;
    private TimeSpan? _finishedAt;

    public string RunId { get; } = runId;

    public string Root { get; } = root;

    public string PlanPath { get; } = planPath;

    public RunPlan Plan { get; } = plan;

    public CancellationTokenSource Cancellation { get; } = new();

    private string _state = "running";
    private string? _message;

    public string State => Volatile.Read(ref _state);

    public string? Message => Volatile.Read(ref _message);

    public bool PersistenceComplete => _persistence.Task.IsCompleted;

    public Task Persistence => _persistence.Task;

    public TimeSpan Elapsed => _finishedAt ?? _clock.Elapsed;

    /// <summary>Whether the request log still has room, so the next sample's response is worth previewing.</summary>
    public bool LogHasRoom => Volatile.Read(ref _logged) < MaxLoggedSamples;

    public void ThreadStarted() => Interlocked.Increment(ref _activeThreads);

    public void ThreadEnded() => Interlocked.Decrement(ref _activeThreads);

    /// <param name="environment">The path of the environment the request was sent with, when the plan lists them.</param>
    public void Add(WorkspacePath request, SampleDto sample, long bytesSent, long bytesReceived, string? environment = null)
    {
        var failed = sample.Error is not null;
        Interlocked.Increment(ref _sent);
        _total.Add(sample.LatencyMs, sample.Status, sample.Error, bytesSent, bytesReceived);
        _requests[request.Value].Add(sample.LatencyMs, sample.Status, sample.Error, bytesSent, bytesReceived);
        if (environment is not null && _environments.Find(e => e.Path == environment).Stats is { } byEnvironment)
        {
            byEnvironment.Add(sample.LatencyMs, sample.Status, sample.Error, bytesSent, bytesReceived);
        }

        _timeline.Add(TimeSpan.FromMilliseconds(sample.AtMs), sample.LatencyMs, failed);

        lock (_gate)
        {
            if (_log.Count < MaxLoggedSamples)
            {
                _log.Add(sample);
                Volatile.Write(ref _logged, _log.Count);
            }

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

    /// <summary>The number of the next saved response file: 1, 2, 3…</summary>
    public int NextResponseNumber() => Interlocked.Increment(ref _responses);

    /// <summary>A copy of the request log: every sample kept so far, in the order they finished.</summary>
    public IReadOnlyList<SampleDto> Log()
    {
        lock (_gate)
        {
            return [.. _log];
        }
    }

    /// <summary>The number of samples sent so far, logged or not.</summary>
    public long Sent => Interlocked.Read(ref _sent);

    public void Finish(string state, string? message = null)
    {
        _finishedAt = _clock.Elapsed;
        Volatile.Write(ref _message, message);
        Volatile.Write(ref _state, state);
    }

    public void CompletePersistence(string? message)
    {
        if (message is not null)
        {
            var current = Message;
            Volatile.Write(ref _message, current is null ? message : $"{current} {message}");
        }

        _persistence.TrySetResult();
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
        // Read the state first: once it is final, every sample is in, so the snapshots below agree.
        var state = State;
        var message = Message;
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
            state,
            startedAt,
            (long)elapsed.TotalMilliseconds,
            Plan.Load.Threads,
            Volatile.Read(ref _activeThreads),
            ToDto(total),
            _requests.Select(pair => new RequestStatsDto(pair.Key, ToDto(pair.Value.Snapshot(elapsed)))).ToList(),
            _timeline.Points().Select(point => new TimelinePointDto(point.Second, point.Samples, point.Errors, Math.Round(point.AverageMs, 1))).ToList(),
            thresholds,
            state == "completed" ? thresholds.All(t => t.Passed) : null,
            failed,
            slowest,
            message,
            _environments.Count == 0
                ? null
                : _environments.Select(e => new EnvironmentStatsDto(e.Path, e.Name, ToDto(e.Stats.Snapshot(elapsed)))).ToList());
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
