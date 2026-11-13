using Dynypost.Application.Common;

namespace Dynypost.Application.Runs;

/// <summary>
/// Waits, up to a limit, for a run to finish and be saved, then returns its numbers like
/// <see cref="GetRunProgress"/>: the final ones, or the live ones if it is still running when the
/// wait ends. A run that isn't in progress is returned at once.
/// </summary>
public sealed class WaitForRun(RunRegistry registry, GetRunProgress progress)
{
    /// <summary>The longest wait accepted, so a caller's request doesn't time out first.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(50);

    public async Task<Result<RunResultDto>> ExecuteAsync(string runId, TimeSpan wait, CancellationToken cancellationToken = default)
    {
        if (registry.Find(runId) is { } run && wait > TimeSpan.Zero)
        {
            try
            {
                await run.Persistence.WaitAsync(wait < MaxWait ? wait : MaxWait, cancellationToken);
            }
            catch (TimeoutException)
            {
                // Still running: report how far it got.
            }
        }

        return await progress.ExecuteAsync(runId, cancellationToken);
    }
}
