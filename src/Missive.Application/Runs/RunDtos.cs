namespace Missive.Application.Runs;

/// <param name="LocalOnly">Whether the file is kept on this computer only, never committed.</param>
public sealed record RunPlanFileDto(string Path, string? Name, string? Error, bool LocalOnly = false);

/// <summary>A plan file open for editing: its text, and why it can't be run yet (if so).</summary>
public sealed record RunPlanDocumentDto(string Path, string Yaml, string? Error);

public sealed record StatsDto(
    long Samples,
    long Errors,
    double ErrorPercent,
    double Min,
    double Average,
    double Median,
    double P90,
    double P95,
    double P99,
    double Max,
    double Throughput,
    long BytesSent,
    long BytesReceived,
    IReadOnlyDictionary<string, long> StatusCodes,
    IReadOnlyDictionary<string, long> ErrorReasons);

public sealed record RequestStatsDto(string Request, StatsDto Stats);

public sealed record TimelinePointDto(int Second, long Samples, long Errors, double AverageMs);

public sealed record ThresholdDto(string Name, string Limit, string Actual, bool Passed);

/// <summary>One request sent during a run. Only failed and slow samples are kept.</summary>
public sealed record SampleDto(
    string Request,
    int Thread,
    int Iteration,
    long AtMs,
    double LatencyMs,
    int? Status,
    string? Error,
    string Method,
    string? Url,
    IReadOnlyDictionary<string, string>? DataRow,
    string? ResponsePreview);

/// <summary>The state of a run, live or finished. <see cref="State"/> is running, completed, cancelled or failed.</summary>
public sealed record RunResultDto(
    string RunId,
    string PlanPath,
    string PlanName,
    string State,
    DateTimeOffset StartedAt,
    long ElapsedMs,
    int Threads,
    int ActiveThreads,
    StatsDto Total,
    IReadOnlyList<RequestStatsDto> Requests,
    IReadOnlyList<TimelinePointDto> Timeline,
    IReadOnlyList<ThresholdDto> Thresholds,
    bool? Passed,
    IReadOnlyList<SampleDto> FailedSamples,
    IReadOnlyList<SampleDto> SlowestSamples,
    string? Message);

public sealed record RunSummaryDto(
    string RunId,
    string PlanPath,
    string PlanName,
    string State,
    DateTimeOffset StartedAt,
    long ElapsedMs,
    bool? Passed,
    long Samples,
    long Errors,
    double ErrorPercent,
    double P95,
    double Throughput);

public sealed record StartedRunDto(string RunId);
