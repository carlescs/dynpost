using Missive.Domain.Workspaces;

namespace Missive.Domain.Runs;

/// <summary>How a data source hands out its entries (CSV rows).</summary>
public enum SelectionMode
{
    Sequential,
    Random,
    PerThread,
}

/// <summary>What a thread does when a sequential or per-thread data source runs out.</summary>
public enum EndBehavior
{
    Recycle,
    Stop,
}

/// <param name="Threads">Concurrent virtual users.</param>
/// <param name="Iterations">Passes over the request list per thread; <c>null</c> when the run is time-based.</param>
/// <param name="Duration">How long each thread keeps iterating; <c>null</c> when the run is count-based.</param>
/// <param name="RampUp">Period over which the threads are started evenly.</param>
/// <param name="ThinkMin">Pause between requests of a thread (the fixed pause when equal to <paramref name="ThinkMax"/>).</param>
/// <param name="MaxRps">Cap on the total requests per second across all threads.</param>
public sealed record RunLoad(
    int Threads,
    int? Iterations,
    TimeSpan? Duration,
    TimeSpan RampUp,
    TimeSpan ThinkMin,
    TimeSpan ThinkMax,
    double? MaxRps);

/// <summary>A CSV file whose header names the variables and whose rows are sets of values.</summary>
public sealed record RunDataSource(WorkspacePath File, SelectionMode Mode, EndBehavior OnEof);

/// <summary>Declarative checks on a response. A failed check makes the sample an error.</summary>
/// <param name="Status">Accepted status codes: <c>201</c> or a class such as <c>2xx</c>.</param>
public sealed record RunAssertion(
    IReadOnlyList<string> Status,
    TimeSpan? MaxTime,
    string? BodyContains,
    string? BodyNotContains)
{
    public bool IsEmpty => Status.Count == 0 && MaxTime is null && BodyContains is null && BodyNotContains is null;
}

/// <summary>Limits that decide whether the whole run passed. Unset limits are not checked.</summary>
public sealed record RunThresholds(
    TimeSpan? P50,
    TimeSpan? P90,
    TimeSpan? P95,
    TimeSpan? P99,
    TimeSpan? Average,
    double? ErrorRatePercent,
    double? MinThroughput)
{
    public static RunThresholds None { get; } = new(null, null, null, null, null, null, null);

    public bool IsEmpty => this == None;
}

/// <summary>A load run plan, as stored in a <c>*.run.yaml</c> file.</summary>
/// <param name="Assertions">Per request path; the key <see cref="RunPlan.AllRequests"/> applies to every request.</param>
public sealed record RunPlan(
    string Name,
    IReadOnlyList<WorkspacePath> Requests,
    RunLoad Load,
    RunDataSource? Data,
    IReadOnlyDictionary<string, RunAssertion> Assertions,
    RunThresholds Thresholds)
{
    public const string AllRequests = "*";
    public const int MaxThreads = 1000;

    /// <summary>Runs with more threads than this need a confirmation, since they run on a desktop machine.</summary>
    public const int WarnThreads = 200;

    public const int MaxIterations = 1_000_000;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

    /// <summary>Problems that make the plan unusable; empty when it is valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("The plan needs a name.");
        }

        if (Requests.Count == 0)
        {
            errors.Add("List at least one request to send.");
        }

        if (Load.Threads is < 1 or > MaxThreads)
        {
            errors.Add($"'load.threads' must be between 1 and {MaxThreads}.");
        }

        if (Load.Iterations is not null && Load.Duration is not null)
        {
            errors.Add("Use either 'load.iterations' or 'load.duration', not both.");
        }
        else if (Load.Iterations is null && Load.Duration is null)
        {
            errors.Add("Set 'load.iterations' or 'load.duration'.");
        }
        else if (Load.Iterations is < 1 or > MaxIterations)
        {
            errors.Add($"'load.iterations' must be between 1 and {MaxIterations}.");
        }
        else if (Load.Duration is { } duration && (duration <= TimeSpan.Zero || duration > MaxDuration))
        {
            errors.Add("'load.duration' must be longer than zero and at most 24h.");
        }

        if (Load.ThinkMin > Load.ThinkMax)
        {
            errors.Add("'load.thinkTime' min can't be greater than max.");
        }

        if (Load.MaxRps is <= 0)
        {
            errors.Add("'load.maxRps' must be greater than zero.");
        }

        foreach (var key in Assertions.Keys)
        {
            if (key != AllRequests && !Requests.Any(request => request.Value == key))
            {
                errors.Add($"'assert' refers to '{key}', which is not in the request list.");
            }
        }

        return errors;
    }

    /// <summary>The assertions that apply to a request: the plan-wide ones, then its own.</summary>
    public IReadOnlyList<RunAssertion> AssertionsFor(WorkspacePath request)
    {
        var list = new List<RunAssertion>();
        if (Assertions.TryGetValue(AllRequests, out var all))
        {
            list.Add(all);
        }

        if (Assertions.TryGetValue(request.Value, out var own))
        {
            list.Add(own);
        }

        return list;
    }
}
