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
/// so work in progress isn't lost; the result says why it can't be run yet. When the plan is
/// renamed and its file name is still the slug of the old name, the file is renamed to match the
/// new name (and its saved runs follow it); files with hand-picked names keep them, and so does a
/// plan that is being run.
/// </summary>
public sealed class SaveRunPlan(
    IRunPlanStore store,
    IRunPlanFormat format,
    IRunStore runs,
    RunRegistry registry,
    ILocalOnlyFiles localFiles,
    CurrentWorkspace current)
{
    private const int MaxAttempts = 1000;

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

        var plan = format.Parse(text);
        var target = await RenameTargetAsync(root.Value, parsed.Value, plan, cancellationToken);

        var written = await store.WriteAsync(root.Value, parsed.Value, text, cancellationToken);
        if (!written.IsSuccess)
        {
            return written.Error!;
        }

        var savedPath = parsed.Value;
        if (target is not null && target != parsed.Value)
        {
            var moved = store.Move(root.Value, parsed.Value, target);
            if (!moved.IsSuccess)
            {
                return moved.Error!;
            }

            savedPath = target;

            // A local-only plan must stay out of git under its new file name.
            var followed = localFiles.Move(root.Value, parsed.Value, target);
            if (!followed.IsSuccess)
            {
                return followed.Error!;
            }

            // Run history is local and only a convenience: a failure here doesn't undo the rename.
            await runs.MovePlanAsync(root.Value, parsed.Value.Value, target.Value, cancellationToken);
        }

        return new RunPlanDocumentDto(savedPath.Value, text, RunPlanChecks.Problem(plan));
    }

    private async Task<WorkspacePath?> RenameTargetAsync(
        string root,
        WorkspacePath path,
        Result<Missive.Domain.Runs.RunPlan> plan,
        CancellationToken cancellationToken)
    {
        if (!plan.IsSuccess || string.IsNullOrWhiteSpace(plan.Value.Name) || !store.Exists(root, path)
            || registry.IsBusy(root, path.Value))
        {
            return null;
        }

        var existingText = await store.ReadAsync(root, path, cancellationToken);
        var existing = existingText.IsSuccess ? format.Parse(existingText.Value) : existingText.Error!;
        if (!existing.IsSuccess || existing.Value.Name == plan.Value.Name)
        {
            return null;
        }

        var oldSlug = Slug.From(existing.Value.Name, "plan");
        var newSlug = Slug.From(plan.Value.Name, "plan");
        if (WorkspaceLayout.RunPlanStem(path.FileName) != oldSlug || newSlug == oldSlug)
        {
            return null;
        }

        var extension = WorkspaceLayout.RunPlanExtension(path.FileName)!;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var candidate = path.Parent.Combine(WorkspaceLayout.RunPlanFileName(plan.Value.Name, attempt, extension));
            if (candidate == path || !store.Exists(root, candidate))
            {
                return candidate;
            }
        }

        return null;
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
