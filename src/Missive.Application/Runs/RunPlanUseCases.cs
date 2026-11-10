using Missive.Application.Common;
using Missive.Application.Sharing;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Runs;

internal static class RunPlanPaths
{
    public static Result<WorkspacePath> Parse(string path)
    {
        if (!WorkspacePath.TryParse(path, out var parsed) || parsed.IsRoot || !WorkspaceLayout.IsRunPlanFileName(parsed.FileName))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{path}' is not a run plan path: run plans are named '*.run.yaml'.");
        }

        return parsed;
    }
}

/// <summary>Lists the run plans of the workspace with their names, or why a file can't be used.</summary>
public sealed class ListRunPlans(IRunPlanStore store, IRunPlanFormat format, ILocalOnlyFiles localFiles, CurrentWorkspace current)
{
    public async Task<Result<IReadOnlyList<RunPlanFileDto>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var paths = store.List(root.Value);
        if (!paths.IsSuccess)
        {
            return paths.Error!;
        }

        var localOnly = LocalOnlyPaths.ListOrEmpty(localFiles, root.Value);
        var plans = new List<RunPlanFileDto>();
        foreach (var path in paths.Value)
        {
            var text = await store.ReadAsync(root.Value, path, cancellationToken);
            var plan = text.IsSuccess ? format.Parse(text.Value) : text.Error!;
            plans.Add(plan.IsSuccess
                ? new RunPlanFileDto(path.Value, plan.Value.Name, null, localOnly.Contains(path))
                : new RunPlanFileDto(path.Value, null, plan.Error!.Message, localOnly.Contains(path)));
        }

        return plans;
    }
}

/// <summary>Opens a run plan for editing. A plan that is invalid still opens, with the reason it can't be run.</summary>
public sealed class OpenRunPlan(IRunPlanStore store, IRunPlanFormat format, CurrentWorkspace current)
{
    public async Task<Result<RunPlanDocumentDto>> ExecuteAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        var parsed = RunPlanPaths.Parse(path);
        if (!root.IsSuccess || !parsed.IsSuccess)
        {
            return root.IsSuccess ? parsed.Error! : root.Error!;
        }

        var text = await store.ReadAsync(root.Value, parsed.Value, cancellationToken);
        if (!text.IsSuccess)
        {
            return text.Error!;
        }

        return new RunPlanDocumentDto(parsed.Value.Value, text.Value, RunPlanChecks.Problem(format.Parse(text.Value)));
    }
}

/// <summary>
/// Saves the text of a run plan. The file is written as typed (so the diff is what the user
/// changed) and only when it differs from what is on disk. A plan that doesn't parse is saved too,
/// so work in progress isn't lost; the result says why it can't be run yet.
/// </summary>
public sealed class SaveRunPlan(IRunPlanStore store, IRunPlanFormat format, CurrentWorkspace current)
{
    public async Task<Result<RunPlanDocumentDto>> ExecuteAsync(string path, string yaml, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        var parsed = RunPlanPaths.Parse(path);
        if (!root.IsSuccess || !parsed.IsSuccess)
        {
            return root.IsSuccess ? parsed.Error! : root.Error!;
        }

        var text = yaml.Replace("\r\n", "\n");
        if (text.Length > 0 && !text.EndsWith('\n'))
        {
            text += "\n";
        }

        var written = await store.WriteAsync(root.Value, parsed.Value, text, cancellationToken);
        if (!written.IsSuccess)
        {
            return written.Error!;
        }

        return new RunPlanDocumentDto(parsed.Value.Value, text, RunPlanChecks.Problem(format.Parse(text)));
    }
}

internal static class RunPlanChecks
{
    /// <summary>Why a parsed plan can't be run, or <c>null</c> when it can.</summary>
    public static string? Problem(Result<Missive.Domain.Runs.RunPlan> plan)
    {
        if (!plan.IsSuccess)
        {
            return plan.Error!.Message;
        }

        var errors = plan.Value.Validate();
        return errors.Count == 0 ? null : string.Join(" ", errors);
    }
}
