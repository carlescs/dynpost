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

    /// <summary>Deletes a plan file.</summary>
    Result<Unit> Delete(string root, WorkspacePath path);
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

/// <summary>Reads the files of a body folder of a run plan, once, before the run starts.</summary>
public interface IRunBodyReader
{
    /// <summary>
    /// The files directly in <paramref name="folder"/> whose names match <paramref name="pattern"/>
    /// (all files when <c>null</c>), in ordinal order of their names. With <paramref name="readText"/>
    /// each file's UTF-8 text is read too. An empty selection, a missing folder or an unreadable or
    /// too large file is returned as an error.
    /// </summary>
    Task<Result<IReadOnlyList<RunBodyFile>>> ReadFolderAsync(
        string root,
        WorkspacePath folder,
        string? pattern,
        bool readText,
        CancellationToken cancellationToken = default);
}

/// <summary>Keeps finished runs in the workspace's local (git-ignored) folder.</summary>
public interface IRunStore
{
    Task<Result<Unit>> SaveAsync(string root, RunResultDto run, CancellationToken cancellationToken = default);

    /// <summary>Keeps the request log of a run next to its result.</summary>
    Task<Result<Unit>> SaveSamplesAsync(string root, string runId, IReadOnlyList<SampleDto> samples, CancellationToken cancellationToken = default);

    /// <summary>The request log of a run; empty when it has none (runs saved before logs were kept).</summary>
    Task<Result<IReadOnlyList<SampleDto>>> GetSamplesAsync(string root, string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the whole body of one response of a run as <paramref name="name"/> (see
    /// <see cref="RunResponseFiles"/>) in the run's responses folder.
    /// </summary>
    Task<Result<Unit>> SaveResponseAsync(string root, string runId, string name, byte[] body, CancellationToken cancellationToken = default);

    /// <summary>A response body saved by <see cref="SaveResponseAsync"/>.</summary>
    Task<Result<byte[]>> GetResponseAsync(string root, string runId, string name, CancellationToken cancellationToken = default);

    /// <summary>The runs of a plan, newest first.</summary>
    Task<Result<IReadOnlyList<RunSummaryDto>>> ListAsync(string root, string planPath, CancellationToken cancellationToken = default);

    Task<Result<RunResultDto>> GetAsync(string root, string runId, CancellationToken cancellationToken = default);

    /// <summary>Points the saved runs of the plan at <paramref name="from"/> to its new path, so its history follows a rename.</summary>
    Task<Result<Unit>> MovePlanAsync(string root, string from, string to, CancellationToken cancellationToken = default);

    /// <summary>Deletes a saved run with its request log and responses; a run that isn't saved is reported as <see cref="RunErrorCodes.RunNotFound"/>.</summary>
    Task<Result<Unit>> DeleteAsync(string root, string runId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the saved runs (with their request logs and responses) of the plan at <paramref name="planPath"/>.</summary>
    Task<Result<Unit>> DeletePlanAsync(string root, string planPath, CancellationToken cancellationToken = default);
}
