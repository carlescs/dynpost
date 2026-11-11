using Missive.Application.Common;
using Missive.Application.Workspaces;

namespace Missive.Application.Runs;

/// <summary>Stops a running plan at any time; the partial results are kept.</summary>
public sealed class CancelRun(RunRegistry registry)
{
    public Result<Unit> Execute(string runId)
    {
        if (registry.Find(runId) is not { } run)
        {
            return new Error(RunErrorCodes.RunNotFound, $"There is no run '{runId}' in progress.");
        }

        run.Cancellation.Cancel();
        return Unit.Value;
    }
}

/// <summary>The current numbers of a run, for the live dashboard. Finished runs are read from the run store.</summary>
public sealed class GetRunProgress(RunRegistry registry, IRunStore store, CurrentWorkspace current)
{
    public async Task<Result<RunResultDto>> ExecuteAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (registry.Find(runId) is { } run)
        {
            if (run.State != "running")
            {
                await run.Persistence.WaitAsync(cancellationToken);
            }

            return run.ToDto();
        }

        var root = current.RequireRoot();
        return root.IsSuccess ? await store.GetAsync(root.Value, runId, cancellationToken) : root.Error!;
    }
}

/// <summary>
/// A page of the request log of a run, live or finished: the samples kept, in the order they
/// finished, optionally only the failed ones.
/// </summary>
public sealed class GetRunSamples(RunRegistry registry, IRunStore store, CurrentWorkspace current)
{
    public const int MaxPageSize = 500;

    public async Task<Result<RunSamplesPageDto>> ExecuteAsync(
        string runId,
        int offset,
        int limit,
        bool failedOnly,
        CancellationToken cancellationToken = default)
    {
        if (registry.Find(runId) is { } run)
        {
            return Page(run.Log(), run.Sent, offset, limit, failedOnly);
        }

        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var saved = await store.GetAsync(root.Value, runId, cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error!;
        }

        var samples = await store.GetSamplesAsync(root.Value, runId, cancellationToken);
        return samples.IsSuccess
            ? Page(samples.Value, saved.Value.Total.Samples, offset, limit, failedOnly)
            : samples.Error!;
    }

    public static RunSamplesPageDto Page(IReadOnlyList<SampleDto> log, long sent, int offset, int limit, bool failedOnly)
    {
        var matching = failedOnly ? log.Where(sample => sample.Error is not null).ToList() : log;
        var start = Math.Clamp(offset, 0, matching.Count);
        var count = Math.Min(Math.Clamp(limit, 1, MaxPageSize), matching.Count - start);
        return new RunSamplesPageDto(start, matching.Count, log.Count, sent, matching.Skip(start).Take(count).ToList());
    }
}

/// <summary>The earlier runs of a plan, newest first.</summary>
public sealed class ListRuns(IRunStore store, CurrentWorkspace current)
{
    public async Task<Result<IReadOnlyList<RunSummaryDto>>> ExecuteAsync(string planPath, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        return root.IsSuccess ? await store.ListAsync(root.Value, planPath, cancellationToken) : root.Error!;
    }
}

/// <summary>The whole body of a response a run saved (its plan has <c>saveResponses</c>).</summary>
public sealed class GetRunResponse(IRunStore store, CurrentWorkspace current)
{
    public async Task<Result<RunResponseDto>> ExecuteAsync(string runId, string file, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!RunResponseFiles.IsValidName(file))
        {
            return new Error(RunErrorCodes.ResponseNotFound, $"'{file}' is not a saved response.");
        }

        var body = await store.GetResponseAsync(root.Value, runId, file, cancellationToken);
        return body.IsSuccess ? new RunResponseDto(Convert.ToBase64String(body.Value)) : body.Error!;
    }
}

/// <summary>
/// Deletes earlier runs of a plan with their request logs and saved responses. Nothing is deleted
/// when one of them is still in progress; otherwise every run is tried and the first failure is reported.
/// </summary>
public sealed class DeleteRun(RunRegistry registry, IRunStore store, CurrentWorkspace current)
{
    public async Task<Result<Unit>> ExecuteAsync(IReadOnlyList<string> runIds, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (runIds.Count == 0)
        {
            return new Error(ErrorCodes.InvalidRequest, "Choose the runs to delete.");
        }

        foreach (var runId in runIds)
        {
            if (registry.Find(runId) is { } run && (run.State == "running" || !run.PersistenceComplete))
            {
                return new Error(ErrorCodes.InvalidRequest, "Stop the run before deleting it.");
            }
        }

        Error? failure = null;
        foreach (var runId in runIds.Distinct(StringComparer.Ordinal))
        {
            var deleted = await store.DeleteAsync(root.Value, runId, cancellationToken);
            if (deleted.IsSuccess)
            {
                registry.Forget(runId);
            }
            else
            {
                failure ??= deleted.Error;
            }
        }

        if (failure is not null)
        {
            return failure;
        }

        return Unit.Value;
    }

    public Task<Result<Unit>> ExecuteAsync(string runId, CancellationToken cancellationToken = default) =>
        ExecuteAsync([runId], cancellationToken);
}

/// <summary>Deletes all the earlier runs of a plan, keeping the plan; refused while the plan is being run.</summary>
public sealed class ClearRuns(RunRegistry registry, IRunStore store, CurrentWorkspace current)
{
    public async Task<Result<Unit>> ExecuteAsync(string planPath, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        var parsed = RunPlanPaths.Parse(planPath);
        if (!root.IsSuccess || !parsed.IsSuccess)
        {
            return root.IsSuccess ? parsed.Error! : root.Error!;
        }

        planPath = parsed.Value.Value;
        if (registry.IsBusy(root.Value, planPath))
        {
            return new Error(ErrorCodes.InvalidRequest, "Stop the run of this plan before clearing its history.");
        }

        var cleared = await store.DeletePlanAsync(root.Value, planPath, cancellationToken);
        if (cleared.IsSuccess)
        {
            registry.ForgetPlan(root.Value, planPath);
        }

        return cleared;
    }
}
