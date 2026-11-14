using Dynpost.Domain.Workspaces;

namespace Dynpost.Domain.Runs;

/// <summary>How a data source hands out its entries (CSV rows or body files).</summary>
public enum SelectionMode
{
    Sequential,
    Random,
    PerThread,
}

/// <summary>How a virtual user takes the plan's requests in each iteration.</summary>
public enum RequestOrder
{
    /// <summary>Every request, in the listed order.</summary>
    Sequential,

    /// <summary>Every request, in a new random order each iteration.</summary>
    Shuffle,

    /// <summary>One request per iteration, picked at random (by its weight).</summary>
    Random,

    /// <summary>One request per iteration, the next one in the list; users start at different requests.</summary>
    RoundRobin,
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

/// <summary>
/// A folder whose files replace a request's body, one file each time the request is sent. Files
/// are taken in ordinal order of their names, optionally filtered by a <c>*</c>/<c>?</c> pattern.
/// </summary>
public sealed record RunBodySource(WorkspacePath Folder, string? Pattern, SelectionMode Mode, EndBehavior OnEof);

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
/// <param name="Order">How each virtual user takes the requests in an iteration.</param>
/// <param name="Weights">
/// Relative chance of each request path with <see cref="RequestOrder.Random"/>; unlisted requests weigh 1.
/// </param>
/// <param name="Disabled">
/// Request paths that stay in the list but are not sent; see <see cref="WithoutDisabledRequests"/>.
/// </param>
/// <param name="SaveResponses">
/// Whether the whole body of each logged response is saved to a file, so it can be opened later
/// (PDFs in the previewer).
/// </param>
/// <param name="Environments">
/// The environment files the run sends its requests with (<c>environments/dev.yaml</c>); empty or
/// <c>null</c> to use the environment selected in the app.
/// </param>
/// <param name="EnvironmentOrder">
/// How each virtual user takes the environments in an iteration, like <see cref="Order"/> does
/// with requests: each environment taken sends the iteration's requests.
/// </param>
/// <param name="EnvironmentWeights">
/// Relative chance of each environment path with <see cref="RequestOrder.Random"/>; unlisted ones weigh 1.
/// </param>
/// <param name="DisabledEnvironments">
/// Environment paths that stay in the list but are not used; see <see cref="WithoutDisabledRequests"/>.
/// </param>
public sealed record RunPlan(
    string Name,
    IReadOnlyList<WorkspacePath> Requests,
    RunLoad Load,
    RunDataSource? Data,
    IReadOnlyDictionary<string, RunAssertion> Assertions,
    RunThresholds Thresholds,
    RequestOrder Order = RequestOrder.Sequential,
    IReadOnlyDictionary<string, double>? Weights = null,
    IReadOnlyList<string>? Disabled = null,
    IReadOnlyDictionary<string, RunBodySource>? Bodies = null,
    bool SaveResponses = false,
    IReadOnlyList<WorkspacePath>? Environments = null,
    RequestOrder EnvironmentOrder = RequestOrder.Sequential,
    IReadOnlyDictionary<string, double>? EnvironmentWeights = null,
    IReadOnlyList<string>? DisabledEnvironments = null)
{
    public const string AllRequests = "*";

    /// <summary>The request paths that are not sent; empty when every request is enabled.</summary>
    public IReadOnlyList<string> DisabledRequests => Disabled ?? [];

    /// <summary>Whether the request is in the list but not sent.</summary>
    public bool IsDisabled(WorkspacePath request) => DisabledRequests.Contains(request.Value, StringComparer.Ordinal);

    /// <summary>The requests that are sent, in the listed order.</summary>
    public IReadOnlyList<WorkspacePath> EnabledRequests => DisabledRequests.Count == 0 ? Requests : Requests.Where(request => !IsDisabled(request)).ToList();

    /// <summary>
    /// The plan as it runs: only the enabled requests, with the assertions, weights and body
    /// folders of disabled ones dropped, and only the enabled environments, with their weights.
    /// </summary>
    public RunPlan WithoutDisabledRequests()
    {
        var plan = this;
        if (DisabledEnvironmentList.Count > 0)
        {
            var off = DisabledEnvironmentList.ToHashSet(StringComparer.Ordinal);
            plan = plan with
            {
                Environments = EnabledEnvironments,
                EnvironmentWeights = EnvironmentWeightsByPath.Where(pair => !off.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal),
                DisabledEnvironments = null,
            };
        }

        if (DisabledRequests.Count == 0)
        {
            return plan;
        }

        var disabled = DisabledRequests.ToHashSet(StringComparer.Ordinal);
        return plan with
        {
            Requests = EnabledRequests,
            Assertions = Assertions.Where(pair => !disabled.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal),
            Weights = RequestWeights.Where(pair => !disabled.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal),
            Bodies = BodySources.Where(pair => !disabled.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal),
            Disabled = null,
        };
    }

    /// <summary>The weight of each request path; empty when none is set.</summary>
    public IReadOnlyDictionary<string, double> RequestWeights => Weights ?? NoWeights;

    private static readonly IReadOnlyDictionary<string, double> NoWeights = new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>The body folder of each request path that has one; empty when none is set.</summary>
    public IReadOnlyDictionary<string, RunBodySource> BodySources => Bodies ?? NoBodies;

    private static readonly IReadOnlyDictionary<string, RunBodySource> NoBodies = new Dictionary<string, RunBodySource>(StringComparer.Ordinal);

    /// <summary>The environments listed in the plan; empty when the run uses the selected one.</summary>
    public IReadOnlyList<WorkspacePath> EnvironmentList => Environments ?? [];

    /// <summary>The environment paths that are listed but not used; empty when every one is used.</summary>
    public IReadOnlyList<string> DisabledEnvironmentList => DisabledEnvironments ?? [];

    /// <summary>Whether the environment is in the list but not used.</summary>
    public bool IsEnvironmentDisabled(WorkspacePath environment) =>
        DisabledEnvironmentList.Contains(environment.Value, StringComparer.Ordinal);

    /// <summary>The environments that are used, in the listed order.</summary>
    public IReadOnlyList<WorkspacePath> EnabledEnvironments =>
        DisabledEnvironmentList.Count == 0 ? EnvironmentList : EnvironmentList.Where(environment => !IsEnvironmentDisabled(environment)).ToList();

    /// <summary>The weight of each environment path; empty when none is set.</summary>
    public IReadOnlyDictionary<string, double> EnvironmentWeightsByPath => EnvironmentWeights ?? NoWeights;

    /// <summary>The relative chance of an environment with <see cref="RequestOrder.Random"/>.</summary>
    public double EnvironmentWeightOf(WorkspacePath environment) =>
        EnvironmentWeightsByPath.TryGetValue(environment.Value, out var weight) ? weight : 1;

    /// <summary>How many environments a virtual user sends the requests of one iteration with.</summary>
    public int EnvironmentsPerIteration =>
        EnabledEnvironments.Count == 0 || EnvironmentOrder is RequestOrder.Random or RequestOrder.RoundRobin ? 1 : EnabledEnvironments.Count;

    /// <summary>How many requests a virtual user sends in one iteration.</summary>
    public int RequestsPerIteration =>
        (Order is RequestOrder.Sequential or RequestOrder.Shuffle ? EnabledRequests.Count : Math.Min(1, EnabledRequests.Count)) * EnvironmentsPerIteration;

    /// <summary>
    /// The most threads a run may use. Threads are async tasks and each keeps one pooled
    /// connection busy at a time, so this is also the run's connection limit; beyond a few
    /// thousand, ephemeral ports and open-file limits of a desktop machine get in the way.
    /// </summary>
    public const int MaxThreads = 5000;

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

        foreach (var environment in EnvironmentList)
        {
            if (!WorkspaceLayout.IsEnvironmentPath(environment))
            {
                errors.Add($"'environments' must list environment files such as {WorkspaceLayout.EnvironmentsFolder}/dev.yaml, not '{environment}'.");
            }
        }

        foreach (var (key, weight) in EnvironmentWeightsByPath)
        {
            if (!EnvironmentList.Any(environment => environment.Value == key))
            {
                errors.Add($"'environmentWeights' refers to '{key}', which is not in the environment list.");
            }
            else if (!double.IsFinite(weight) || weight < 0)
            {
                errors.Add($"The weight of '{key}' must be zero or more.");
            }
        }

        foreach (var path in DisabledEnvironmentList)
        {
            if (!EnvironmentList.Any(environment => environment.Value == path))
            {
                errors.Add($"'disabledEnvironments' refers to '{path}', which is not in the environment list.");
            }
        }

        if (EnvironmentList.Count > 0 && EnabledEnvironments.Count == 0)
        {
            errors.Add("Enable at least one environment, or remove them all to use the selected one.");
        }

        if (EnvironmentOrder == RequestOrder.Random && EnabledEnvironments.Count > 0 && EnabledEnvironments.All(environment => EnvironmentWeightOf(environment) <= 0))
        {
            errors.Add("At least one environment needs a weight greater than zero.");
        }

        if (Requests.Count == 0)
        {
            errors.Add("List at least one request to send.");
        }
        else if (EnabledRequests.Count == 0)
        {
            errors.Add("Enable at least one request to send.");
        }

        foreach (var path in DisabledRequests)
        {
            if (!Requests.Any(request => request.Value == path))
            {
                errors.Add($"'disabled' refers to '{path}', which is not in the request list.");
            }
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

        foreach (var (key, weight) in RequestWeights)
        {
            if (!Requests.Any(request => request.Value == key))
            {
                errors.Add($"'weights' refers to '{key}', which is not in the request list.");
            }
            else if (!double.IsFinite(weight) || weight < 0)
            {
                errors.Add($"The weight of '{key}' must be zero or more.");
            }
        }

        foreach (var key in BodySources.Keys)
        {
            if (!Requests.Any(request => request.Value == key))
            {
                errors.Add($"'bodies' refers to '{key}', which is not in the request list.");
            }
        }

        if (Order == RequestOrder.Random && EnabledRequests.Count > 0 && EnabledRequests.All(request => WeightOf(request) <= 0))
        {
            errors.Add("At least one request needs a weight greater than zero.");
        }

        return errors;
    }

    /// <summary>The relative chance of a request with <see cref="RequestOrder.Random"/>.</summary>
    public double WeightOf(WorkspacePath request) => RequestWeights.TryGetValue(request.Value, out var weight) ? weight : 1;

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
