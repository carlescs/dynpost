using System.Collections.Concurrent;
using System.Text;
using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Application.Http;
using Missive.Application.OAuth2;
using Missive.Application.Workspaces;
using Missive.Domain.Http;
using Missive.Domain.Requests;
using Missive.Domain.Runs;
using Missive.Domain.Workspaces;

namespace Missive.Application.Runs;

public static class RunErrorCodes
{
    /// <summary>The run is large enough to need the user's confirmation first.</summary>
    public const string ConfirmationRequired = "confirmation_required";

    public const string RunNotFound = "run_not_found";

    /// <summary>The run didn't save the response asked for.</summary>
    public const string ResponseNotFound = "response_not_found";
}

/// <summary>Keeps track of the runs of this session: the one(s) executing and the recent finished ones.</summary>
public sealed class RunRegistry
{
    private const int KeepFinished = 10;

    private readonly ConcurrentDictionary<string, ActiveRun> _runs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Root, string PlanPath), byte> _reservations = new();

    public void Add(ActiveRun run)
    {
        _runs[run.RunId] = run;
        var finished = _runs.Values.Where(r => r.State != "running" && r.PersistenceComplete).OrderBy(r => r.Elapsed).ToList();
        foreach (var old in finished.Take(Math.Max(0, finished.Count - KeepFinished)))
        {
            _runs.TryRemove(old.RunId, out _);
        }
    }

    public ActiveRun? Find(string runId) => _runs.GetValueOrDefault(runId);

    /// <summary>Whether a run of the plan is starting, going on or still being saved.</summary>
    public bool IsBusy(string root, string planPath) =>
        _reservations.ContainsKey((root, planPath))
        || _runs.Values.Any(r => r.Root == root && r.PlanPath == planPath && (r.State == "running" || !r.PersistenceComplete));

    public IDisposable? TryReserve(string root, string planPath)
    {
        var key = (root, planPath);
        if (!_reservations.TryAdd(key, 0))
        {
            return null;
        }

        if (_runs.Values.Any(r => r.State == "running" && r.Root == root && r.PlanPath == planPath))
        {
            _reservations.TryRemove(key, out _);
            return null;
        }

        return new PlanReservation(this, key);
    }

    private void ReleaseReservation((string Root, string PlanPath) key) => _reservations.TryRemove(key, out _);

    private sealed class PlanReservation(RunRegistry registry, (string Root, string PlanPath) key) : IDisposable
    {
        private RunRegistry? _registry = registry;

        public void Dispose() => Interlocked.Exchange(ref _registry, null)?.ReleaseReservation(key);
    }
}

/// <summary>
/// Starts a load run of a plan and returns at once; the run goes on in the background and is
/// followed with <see cref="GetRunProgress"/>. Each request is prepared and sent exactly like
/// <see cref="SendRequest"/> does (environment variables, auth, OAuth 2.0 tokens), with the CSV
/// row of the iteration laid over the environment's variables. Runs don't use cookies: every
/// virtual user starts clean and nothing is written to the environment's cookie jar or captures.
/// Runs of more than <see cref="RunPlan.WarnThreads"/> threads need <paramref name="confirmLarge"/>.
/// </summary>
public sealed class StartRun(
    IRunPlanStore store,
    IRunPlanFormat format,
    IRunDataReader dataReader,
    IWorkspaceRepository repository,
    IHttpSender sender,
    ActiveEnvironment environment,
    OAuth2Tokens tokens,
    IRunStore runStore,
    RunRegistry registry,
    CurrentWorkspace current,
    TimeProvider time)
{
    public async Task<Result<StartedRunDto>> ExecuteAsync(string path, bool confirmLarge, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        var planPath = RunPlanPaths.Parse(path);
        if (!root.IsSuccess || !planPath.IsSuccess)
        {
            return root.IsSuccess ? planPath.Error! : root.Error!;
        }

        using var reservation = registry.TryReserve(root.Value, planPath.Value.Value);
        if (reservation is null)
        {
            return new Error(ErrorCodes.AlreadyExists, "This plan is already running.");
        }

        var text = await store.ReadAsync(root.Value, planPath.Value, cancellationToken);
        if (!text.IsSuccess)
        {
            return text.Error!;
        }

        var parsed = format.Parse(text.Value);
        if (!parsed.IsSuccess)
        {
            return parsed.Error!;
        }

        var plan = parsed.Value;
        var problems = plan.Validate();
        if (problems.Count > 0)
        {
            return new Error(ErrorCodes.InvalidFile, string.Join(" ", problems));
        }

        if (plan.Load.Threads > RunPlan.WarnThreads && !confirmLarge)
        {
            return new Error(
                RunErrorCodes.ConfirmationRequired,
                $"This run starts {plan.Load.Threads} threads, which is a lot for a desktop app.");
        }

        var definitions = new Dictionary<string, HttpRequestDefinition>(StringComparer.Ordinal);
        var broken = new List<string>();
        foreach (var request in plan.Requests.DistinctBy(r => r.Value))
        {
            if (!repository.FileExists(root.Value, request))
            {
                broken.Add($"'{request}' does not exist");
                continue;
            }

            var read = await repository.ReadRequestAsync(root.Value, request, cancellationToken);
            if (read.IsSuccess)
            {
                definitions[request.Value] = read.Value;
            }
            else
            {
                broken.Add(read.Error!.Message);
            }
        }

        if (broken.Count > 0)
        {
            return new Error(ErrorCodes.InvalidRequest, $"The plan refers to requests that can't be used: {string.Join("; ", broken)}.");
        }

        IReadOnlyList<IReadOnlyDictionary<string, string>> rows = [];
        if (plan.Data is { } data)
        {
            var read = await dataReader.ReadCsvAsync(root.Value, data.File, cancellationToken);
            if (!read.IsSuccess)
            {
                return read.Error!;
            }

            if (read.Value.Count == 0)
            {
                return new Error(ErrorCodes.InvalidFile, $"The data file '{data.File}' has no rows.");
            }

            rows = read.Value;
        }

        var variables = await environment.LoadAsync(cancellationToken);
        if (!variables.IsSuccess)
        {
            return variables.Error!;
        }

        var startedAt = time.GetUtcNow();
        var runId = $"{startedAt:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        var run = new ActiveRun(runId, root.Value, planPath.Value.Value, plan, startedAt);
        registry.Add(run);
        _ = Task.Run(() => ExecuteRunAsync(run, definitions, rows, variables.Value), CancellationToken.None);
        return new StartedRunDto(runId);
    }

    private async Task ExecuteRunAsync(
        ActiveRun run,
        Dictionary<string, HttpRequestDefinition> definitions,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        ActiveVariables variables)
    {
        var plan = run.Plan;
        var token = run.Cancellation.Token;
        var cursor = plan.Data is { } data ? new DataCursor(rows.Count, data.Mode, data.OnEof, plan.Load.Threads) : null;
        var limiter = plan.Load.MaxRps is { } rps ? new RateLimiter(rps) : null;
        var context = new ThreadContext(run, definitions, rows, variables, cursor, limiter, new RequestPicker(plan));

        try
        {
            var threads = Enumerable.Range(0, plan.Load.Threads).Select(t => Task.Run(() => RunThreadAsync(context, t), CancellationToken.None));
            await Task.WhenAll(threads);
            run.Finish(token.IsCancellationRequested ? "cancelled" : "completed");
        }
        catch (Exception ex)
        {
            run.Finish("failed", ex.Message);
        }

        // The partial results of a cancelled run are kept too.
        try
        {
            var saved = await runStore.SaveAsync(run.Root, run.ToDto(), CancellationToken.None);
            if (saved.IsSuccess)
            {
                saved = await runStore.SaveSamplesAsync(run.Root, run.RunId, run.Log(), CancellationToken.None);
            }

            run.CompletePersistence(saved.IsSuccess ? null : $"Run results could not be saved: {saved.Error!.Message}");
        }
        catch (Exception ex)
        {
            run.CompletePersistence($"Run results could not be saved: {ex.Message}");
        }
    }

    private sealed record ThreadContext(
        ActiveRun Run,
        Dictionary<string, HttpRequestDefinition> Definitions,
        IReadOnlyList<IReadOnlyDictionary<string, string>> Rows,
        ActiveVariables Variables,
        DataCursor? Cursor,
        RateLimiter? Limiter,
        RequestPicker Picker);

    private async Task RunThreadAsync(ThreadContext context, int thread)
    {
        var run = context.Run;
        var load = run.Plan.Load;
        var token = run.Cancellation.Token;
        var random = new Random(thread * 7919);
        run.ThreadStarted();
        try
        {
            var delay = RunScheduling.StartDelay(thread, load.Threads, load.RampUp);
            if (delay > TimeSpan.Zero && !await WaitWithinRunAsync(run, delay, token))
            {
                return;
            }

            var first = true;
            for (var iteration = 0; load.Iterations is null || iteration < load.Iterations; iteration++)
            {
                if (TimeUp(run))
                {
                    break;
                }

                IReadOnlyDictionary<string, string>? row = null;
                if (context.Cursor is { } cursor)
                {
                    if (cursor.Next(thread) is not { } index)
                    {
                        break;
                    }

                    row = context.Rows[index];
                }

                var values = row is null
                    ? context.Variables.Values
                    : context.Variables.Values.Concat(row).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);

                foreach (var request in context.Picker.ForIteration(thread, iteration, random))
                {
                    if (!first)
                    {
                        var think = RunScheduling.ThinkTime(load, random);
                        if (think > TimeSpan.Zero && !await WaitWithinRunAsync(run, think, token))
                        {
                            return;
                        }
                    }

                    first = false;
                    if (TimeUp(run))
                    {
                        return;
                    }

                    if (context.Limiter is { } limiter)
                    {
                        var wait = limiter.Reserve(run.Now.Ticks);
                        if (wait > TimeSpan.Zero && !await WaitWithinRunAsync(run, wait, token))
                        {
                            return;
                        }
                    }

                    if (TimeUp(run))
                    {
                        return;
                    }

                    await SampleAsync(context, request, thread, iteration, values, row);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            run.ThreadEnded();
        }
    }

    private static bool TimeUp(ActiveRun run) =>
        run.Cancellation.IsCancellationRequested || (run.Plan.Load.Duration is { } duration && run.Now >= duration);

    private static async Task<bool> WaitWithinRunAsync(ActiveRun run, TimeSpan delay, CancellationToken token)
    {
        var remaining = run.Plan.Load.Duration is { } duration ? duration - run.Now : (TimeSpan?)null;
        if (remaining is { } timeLeft)
        {
            if (timeLeft <= TimeSpan.Zero)
            {
                return false;
            }

            if (delay >= timeLeft)
            {
                await Task.Delay(timeLeft, token);
                return false;
            }
        }

        await Task.Delay(delay, token);
        return !TimeUp(run);
    }

    private async Task SampleAsync(
        ThreadContext context,
        WorkspacePath path,
        int thread,
        int iteration,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, string>? row)
    {
        var run = context.Run;
        var token = run.Cancellation.Token;
        var definition = context.Definitions[path.Value];
        var method = definition.Body.Type == BodyType.Graphql ? "POST" : definition.Method.ToName();
        var at = run.Now;

        SampleDto Failure(string reason, double latency = 0, int? status = null, string? url = null, string? preview = null) =>
            new(path.Value, thread, iteration, (long)at.TotalMilliseconds, Math.Round(latency, 1), status, reason, method, url, row, preview);

        var oauth = definition.Auth as OAuth2Auth;
        var prepared = RequestPreparation.Prepare(definition, values, oauth is null ? null : "token", run.Root);
        if (!prepared.IsValid)
        {
            run.Add(path, Failure(context.Variables.Explain(prepared.Errors, prepared.MissingVariables)), 0, 0);
            return;
        }

        var outgoing = prepared.Request!;
        if (oauth is not null)
        {
            var resolution = OAuth2Flow.Resolve(oauth, values);
            if (!resolution.IsValid)
            {
                run.Add(path, Failure(context.Variables.Explain(resolution.Errors, resolution.MissingVariables)), 0, 0);
                return;
            }

            var accessToken = await tokens.GetAsync(resolution.Settings!, run.Root, context.Variables.EnvironmentId, token);
            if (!accessToken.IsSuccess)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                run.Add(path, Failure(accessToken.Error!.Message), 0, 0);
                return;
            }

            outgoing = RequestPreparation.Prepare(definition, values, accessToken.Value.AccessToken, run.Root).Request!;
        }

        IReadOnlyCollection<string> sensitiveParams = definition.Auth is ApiKeyAuth { In: ApiKeyLocation.Query } apiKey
            ? [VariableTemplate.Resolve(apiKey.Key, values, new SortedSet<string>(StringComparer.Ordinal)).Trim()]
            : [];
        var url = SecretMasking.MaskUrl(outgoing.Url, sensitiveParams, context.Variables.SecretValues ?? []);
        var sent = outgoing.Body is TextBody text ? Encoding.UTF8.GetByteCount(text.Content) : 0;
        if (TimeUp(run))
        {
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Result<HttpResponseData> response;
        try
        {
            response = await sender.SendAsync(outgoing, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }

        var latency = watch.Elapsed.TotalMilliseconds;
        if (!response.IsSuccess)
        {
            run.Add(path, Failure(response.Error!.Message, latency, url: url), sent, 0);
            return;
        }

        var data = response.Value;
        var assertions = run.Plan.AssertionsFor(path);
        var contentType = data.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value;
        var (mediaType, charset) = ResponseBodyDecoder.ParseContentType(contentType);
        string? body = null;
        if (assertions.Any(a => a.BodyContains is not null || a.BodyNotContains is not null))
        {
            body = ResponseBodyDecoder.Decode(data.Body, mediaType, charset).Body;
        }
        var reason = FailureReason(assertions, data, body, latency);
        // Only samples that are kept (in the log, or as failures) get a preview of their response.
        var kept = reason is not null || run.LogHasRoom;
        // A PDF's bytes make no sense as text; it can be opened from its saved file instead.
        var pdf = mediaType == RunResponseFiles.PdfMediaType;
        string? file = null;
        if (kept && run.Plan.SaveResponses && data.Body.Length > 0)
        {
            var name = RunResponseFiles.Name(run.NextResponseNumber(), mediaType);
            var saved = await runStore.SaveResponseAsync(run.Root, run.RunId, name, data.Body, CancellationToken.None);
            file = saved.IsSuccess ? name : null;
        }
        var sample = new SampleDto(
            path.Value,
            thread,
            iteration,
            (long)at.TotalMilliseconds,
            Math.Round(latency, 1),
            data.StatusCode,
            reason,
            method,
            url,
            row,
            kept && !pdf ? ActiveRun.Preview(data.Body) : null,
            mediaType,
            file);
        run.Add(path, sample, data.BytesSent > 0 ? data.BytesSent : sent, data.Body.LongLength);
    }

    /// <summary>
    /// Why a response counts as an error: a failed assertion, or, when the request has no status
    /// assertion, a 4xx/5xx status. <c>null</c> when it is fine.
    /// </summary>
    internal static string? FailureReason(IReadOnlyList<RunAssertion> assertions, HttpResponseData response, string? body, double latencyMs)
    {
        var statusChecked = assertions.Any(a => a.Status.Count > 0);
        if (!statusChecked && response.StatusCode >= 400)
        {
            return $"HTTP {response.StatusCode}";
        }

        foreach (var assertion in assertions)
        {
            if (assertion.Status.Count > 0 && !assertion.Status.Any(p => RunText.StatusMatches(p, response.StatusCode)))
            {
                return $"Expected status {string.Join(" or ", assertion.Status)}, got {response.StatusCode}";
            }

            if (assertion.MaxTime is { } max && latencyMs > max.TotalMilliseconds)
            {
                return $"Took {latencyMs:0}ms, more than {RunText.FormatDuration(max)}";
            }

            if (assertion.BodyContains is { } contains && body is not null && !body.Contains(contains, StringComparison.Ordinal))
            {
                return $"Body doesn't contain '{contains}'";
            }

            if (assertion.BodyNotContains is { } notContains && body is not null && body.Contains(notContains, StringComparison.Ordinal))
            {
                return $"Body contains '{notContains}'";
            }
        }

        return null;
    }
}
