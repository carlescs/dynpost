using Missive.Application.Common;
using Missive.Domain.Runs;
using Missive.Domain.Workspaces;

namespace Missive.Application.Runs;

/// <summary>Reads and writes the text of run plan files (<c>*.run.yaml</c>) in a workspace.</summary>
public interface IRunPlanStore
{
    /// <summary>The paths of all run plan files, in order.</summary>
    Result<IReadOnlyList<WorkspacePath>> List(string root);

    Task<Result<string>> ReadAsync(string root, WorkspacePath path, CancellationToken cancellationToken = default);

    /// <summary>Writes the file unless it already has this text; returns whether it changed.</summary>
    Task<Result<bool>> WriteAsync(string root, WorkspacePath path, string text, CancellationToken cancellationToken = default);

    bool Exists(string root, WorkspacePath path);

    /// <summary>Renames a plan file; fails rather than overwrite an existing file.</summary>
    Result<Unit> Move(string root, WorkspacePath from, WorkspacePath to);
}

/// <summary>The file format of a run plan.</summary>
public interface IRunPlanFormat
{
    /// <summary>Parses a plan file; problems with its shape are returned as an error.</summary>
    Result<RunPlan> Parse(string text);
}

/// <summary>Reads a CSV file of a workspace: the header row names the variables, each other row is a set of values.</summary>
public interface IRunDataReader
{
    Task<Result<IReadOnlyList<IReadOnlyDictionary<string, string>>>> ReadCsvAsync(
        string root,
        WorkspacePath path,
        CancellationToken cancellationToken = default);
}

/// <summary>Keeps finished runs in the workspace's local (git-ignored) folder.</summary>
public interface IRunStore
{
    Task<Result<Unit>> SaveAsync(string root, RunResultDto run, CancellationToken cancellationToken = default);

    /// <summary>The runs of a plan, newest first.</summary>
    Task<Result<IReadOnlyList<RunSummaryDto>>> ListAsync(string root, string planPath, CancellationToken cancellationToken = default);

    Task<Result<RunResultDto>> GetAsync(string root, string runId, CancellationToken cancellationToken = default);

    /// <summary>Points the saved runs of the plan at <paramref name="from"/> to its new path, so its history follows a rename.</summary>
    Task<Result<Unit>> MovePlanAsync(string root, string from, string to, CancellationToken cancellationToken = default);
}
