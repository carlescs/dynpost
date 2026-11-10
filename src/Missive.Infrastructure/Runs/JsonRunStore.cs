using System.Text.Json;
using System.Text.Json.Serialization;
using Missive.Application.Common;
using Missive.Application.Runs;
using Missive.Domain.Workspaces;
using Missive.Infrastructure.Git;
using static Missive.Infrastructure.Workspaces.WorkspaceFiles;

namespace Missive.Infrastructure.Runs;

/// <summary>Keeps each finished run as <c>.missive/runs/&lt;run id&gt;.json</c>, which is git-ignored.</summary>
public sealed class JsonRunStore : IRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<Result<Unit>> SaveAsync(string root, RunResultDto run, CancellationToken cancellationToken = default)
    {
        try
        {
            await GitIgnoreFile.EnsureIgnoredAsync(root, cancellationToken);
            await WriteIfChangedAsync(FilePath(root, run.RunId), JsonSerializer.Serialize(run, JsonOptions) + "\n", cancellationToken);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not save the run: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<RunSummaryDto>>> ListAsync(string root, string planPath, CancellationToken cancellationToken = default)
    {
        var summaries = new List<RunSummaryDto>();
        try
        {
            var folder = FullPath(root, RunsFolder);
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
                {
                    var run = await ReadAsync(file, cancellationToken);
                    if (run is not null && run.PlanPath == planPath)
                    {
                        summaries.Add(new RunSummaryDto(
                            run.RunId,
                            run.PlanPath,
                            run.PlanName,
                            run.State,
                            run.StartedAt,
                            run.ElapsedMs,
                            run.Passed,
                            run.Total.Samples,
                            run.Total.Errors,
                            run.Total.ErrorPercent,
                            run.Total.P95,
                            run.Total.Throughput));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not list the runs: {ex.Message}");
        }

        return summaries.OrderByDescending(s => s.StartedAt).ToList();
    }

    public async Task<Result<RunResultDto>> GetAsync(string root, string runId, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(runId))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{runId}' is not a run id.");
        }

        try
        {
            return await ReadAsync(FilePath(root, runId), cancellationToken)
                ?? (Result<RunResultDto>)new Error(RunErrorCodes.RunNotFound, $"There is no run '{runId}'.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read the run: {ex.Message}");
        }
    }

    public async Task<Result<Unit>> MovePlanAsync(string root, string from, string to, CancellationToken cancellationToken = default)
    {
        try
        {
            var folder = FullPath(root, RunsFolder);
            if (!Directory.Exists(folder))
            {
                return Unit.Value;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                var run = await ReadAsync(file, cancellationToken);
                if (run is not null && run.PlanPath == from)
                {
                    await WriteIfChangedAsync(file, JsonSerializer.Serialize(run with { PlanPath = to }, JsonOptions) + "\n", cancellationToken);
                }
            }

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not move the runs of '{from}': {ex.Message}");
        }
    }

    private static WorkspacePath RunsFolder { get; } = WorkspacePath.Root.Combine(WorkspaceLayout.LocalFolder).Combine("runs");

    private static string FilePath(string root, string runId) => FullPath(root, RunsFolder.Combine(runId + ".json"));

    private static bool IsValidId(string runId) => runId.Length is > 0 and <= 64 && runId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static async Task<RunResultDto?> ReadAsync(string fullPath, CancellationToken cancellationToken)
    {
        var text = await ReadIfExistsAsync(fullPath, cancellationToken);
        try
        {
            return text is null ? null : JsonSerializer.Deserialize<RunResultDto>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
