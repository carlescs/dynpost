namespace Dynpost.Application.Runs;

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

/// <summary>
/// One request sent during a run. The run result keeps only failed and slow samples; the request
/// log keeps the first <see cref="ActiveRun.MaxLoggedSamples"/> of them all.
/// </summary>
/// <param name="BodyFile">The workspace path of the body folder file the request sent, if any.</param>
/// <param name="ResponsePreview">The start of the response body as text; <c>null</c> for PDFs.</param>
/// <param name="ContentType">The media type of the response, when it has one.</param>
/// <param name="ResponseFile">
/// The name of the file the whole response body was saved to, when the plan saves responses
/// (<see cref="Domain.Runs.RunPlan.SaveResponses"/>); read it with <see cref="GetRunResponse"/>.
/// </param>
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
    string? ResponsePreview,
    string? BodyFile = null,
    string? ContentType = null,
    string? ResponseFile = null);

/// <summary>A response body saved by a run, as base64.</summary>
public sealed record RunResponseDto(string Body);

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

/// <summary>A page of the request log of a run.</summary>
/// <param name="Offset">The position of the first sample of the page among the matching ones.</param>
/// <param name="Matching">How many logged samples match the filter.</param>
/// <param name="Logged">How many samples the log keeps (at most <see cref="ActiveRun.MaxLoggedSamples"/>).</param>
/// <param name="Sent">How many requests the run sent, logged or not.</param>
public sealed record RunSamplesPageDto(
    int Offset,
    int Matching,
    int Logged,
    long Sent,
    IReadOnlyList<SampleDto> Samples);

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
