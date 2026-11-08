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
            return run.ToDto();
        }

        var root = current.RequireRoot();
        return root.IsSuccess ? await store.GetAsync(root.Value, runId, cancellationToken) : root.Error!;
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
