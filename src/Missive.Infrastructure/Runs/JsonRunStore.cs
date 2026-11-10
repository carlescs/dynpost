using System.Text.Json;
using System.Text.Json.Serialization;
using Missive.Application.Common;
using Missive.Application.Runs;
using Missive.Domain.Workspaces;
using Missive.Infrastructure.Git;
using static Missive.Infrastructure.Workspaces.WorkspaceFiles;

namespace Missive.Infrastructure.Runs;

/// <summary>
/// Keeps each finished run as <c>.missive/runs/&lt;run id&gt;.json</c> and its request log as
/// <c>.missive/runs/samples/&lt;run id&gt;.json</c>; the responses a run saves (when its plan has
/// <c>saveResponses</c>) go to <c>.missive/runs/responses/&lt;run id&gt;/</c>. All are git-ignored.
/// </summary>
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

    public async Task<Result<Unit>> SaveSamplesAsync(string root, string runId, IReadOnlyList<SampleDto> samples, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(runId))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{runId}' is not a run id.");
        }

        try
        {
            await GitIgnoreFile.EnsureIgnoredAsync(root, cancellationToken);
            await WriteIfChangedAsync(SamplesPath(root, runId), JsonSerializer.Serialize(samples, JsonOptions) + "\n", cancellationToken);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not save the request log of the run: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<SampleDto>>> GetSamplesAsync(string root, string runId, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(runId))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{runId}' is not a run id.");
        }

        try
        {
            var text = await ReadIfExistsAsync(SamplesPath(root, runId), cancellationToken);
            IReadOnlyList<SampleDto> samples = text is null ? [] : JsonSerializer.Deserialize<List<SampleDto>>(text, JsonOptions) ?? [];
            return Result<IReadOnlyList<SampleDto>>.Success(samples);
        }
        catch (JsonException)
        {
            return Result<IReadOnlyList<SampleDto>>.Success([]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read the request log of the run: {ex.Message}");
        }
    }

    public async Task<Result<Unit>> SaveResponseAsync(string root, string runId, string name, byte[] body, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(runId) || !RunResponseFiles.IsValidName(name))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{runId}/{name}' is not a run response.");
        }

        try
        {
            await GitIgnoreFile.EnsureIgnoredAsync(root, cancellationToken);
            var path = ResponsePath(root, runId, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, body, cancellationToken);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not save the response: {ex.Message}");
        }
    }

    public async Task<Result<byte[]>> GetResponseAsync(string root, string runId, string name, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(runId) || !RunResponseFiles.IsValidName(name))
        {
            return new Error(ErrorCodes.InvalidPath, $"'{runId}/{name}' is not a run response.");
        }

        try
        {
            var path = ResponsePath(root, runId, name);
            return File.Exists(path)
                ? await File.ReadAllBytesAsync(path, cancellationToken)
                : new Error(RunErrorCodes.ResponseNotFound, $"The response '{name}' of the run was not saved or has been deleted.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read the response: {ex.Message}");
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

    public async Task<Result<Unit>> DeletePlanAsync(string root, string planPath, CancellationToken cancellationToken = default)
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
                if (run is not null && run.PlanPath == planPath)
                {
                    File.Delete(file);
                    var samples = SamplesPath(root, run.RunId);
                    if (File.Exists(samples))
                    {
                        File.Delete(samples);
                    }

                    var responses = ResponsesFolder(root, run.RunId);
                    if (Directory.Exists(responses))
                    {
                        Directory.Delete(responses, recursive: true);
                    }
                }
            }

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not delete the runs of '{planPath}': {ex.Message}");
        }
    }

    private static WorkspacePath RunsFolder { get; } = WorkspacePath.Root.Combine(WorkspaceLayout.LocalFolder).Combine("runs");

    private static string FilePath(string root, string runId) => FullPath(root, RunsFolder.Combine(runId + ".json"));

    private static string SamplesPath(string root, string runId) => FullPath(root, RunsFolder.Combine("samples").Combine(runId + ".json"));

    private static string ResponsesFolder(string root, string runId) => FullPath(root, RunsFolder.Combine("responses").Combine(runId));

    private static string ResponsePath(string root, string runId, string name) => Path.Combine(ResponsesFolder(root, runId), name);

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
