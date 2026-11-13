using System.ComponentModel;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Application.Folders;
using Missive.Application.History;
using Missive.Application.Git;
using Missive.Application.Http;
using Missive.Application.Requests;
using Missive.Application.Runs;
using Missive.Application.Workspaces;
using Missive.Desktop.Bridge;
using Missive.Domain.Workspaces;

namespace Missive.Desktop.Mcp;

/// <summary>
/// The tools the MCP server offers, each mapped to the use case that handles it — the MCP
/// counterpart of <see cref="BridgeHandlers"/>. Tools act on the workspace open in the app; tools
/// that change it tell the UI (<see cref="HostEvents"/>) so it shows the change. Committing,
/// pulling and pushing stay out: the app never does them without an explicit user action.
/// </summary>
internal static class McpTools
{
    /// <summary>How tool arguments are read and results written: the bridge's JSON, strict about required and non-null values.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        // Results are JSON for an AI assistant, never HTML: keep non-ASCII text and quotes readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The most seconds of a run's timeline shown to the AI assistant: the latest ones.</summary>
    public const int MaxTimelinePoints = 120;

    /// <summary>The most logged requests of a run shown to the AI assistant at once.</summary>
    public const int MaxSamplesPage = 100;

    private const string RunPlanShape =
        "A plan is YAML: name; requests: [request paths such as requests/shop/list-products.yaml]; disabled?: [paths not sent]; "
        + "order?: sequential|shuffle|random|roundRobin; weights?: { path: number }; load: { threads, iterations or duration (2m), rampUp?, thinkTime?, maxRps? }; "
        + "data?: { file: a CSV whose columns become {{variables}}, mode?: sequential|random|perThread, onEof?: recycle|stop }; "
        + "bodies?: { requestPath: { folder, pattern?, mode?, onEof? } }; saveResponses?: bool; "
        + "assert?: { \"*\" or path: { status: 200-299, maxTime: 800ms } }; thresholds?: { p95: 500ms, errorRate: 1%, throughput: 50 }. "
        + "Plans live in plans/ and are named <name>.run.yaml.";

    /// <summary>The most commits listed at once.</summary>
    public const int MaxCommitsPage = 100;

    /// <summary>The most characters of a response body shown to the AI assistant.</summary>
    public const int MaxBodyChars = 64 * 1024;

    private const string RequestShape =
        "A request is { name, method, url, params: [{ key, value, enabled }], headers: [{ key, value, enabled }], "
        + "auth: { type: none|basic|bearer|apiKey|oauth2, ... }, body: { type: none|json|text|form|urlencoded|graphql|binary, content }, "
        + "description?, captures?: [{ variable, expression }] }. Values may use {{variable}} references, resolved with the selected environment when sent.";

    public static IReadOnlyList<McpTool> Create(IServiceProvider services, HostEvents events)
    {
        T Use<T>() where T : notnull => services.GetRequiredService<T>();

        return new Builder(events)
            .Add<NoArguments, WorkspaceDto>(
                "get_workspace", "Get workspace",
                "Lists the workspace open in Missive: its name, folder and the tree of requests (in requests/) and their collection folders. "
                + "Paths are relative to the workspace folder, with / separators (requests/users/get-user.yaml).",
                (_, ct) => Use<GetWorkspaceTree>().ExecuteAsync(ct),
                readOnly: true)
            .Add<PathArguments, RequestDto>(
                "read_request", "Read request",
                "Reads a saved request file. " + RequestShape,
                (p, ct) => Use<LoadRequest>().ExecuteAsync(p.Path, ct),
                readOnly: true)
            .Add<CreateRequestArguments, SavedRequestDto>(
                "create_request", "Create request",
                "Saves a new request in a folder of requests/ (requests itself for the top level); the file is named after the request. "
                + "Returns its path. " + RequestShape,
                (p, ct) => Use<CreateRequest>().ExecuteAsync(p.Folder, p.Request, ct),
                changed: (_, saved) => [saved.Path])
            .Add<SaveRequestArguments, SavedRequestDto>(
                "save_request", "Save request",
                "Replaces the content of a saved request file; renaming the request may rename the file. Read it first and send it back whole, changed. "
                + "Returns its (possibly new) path. " + RequestShape,
                (p, ct) => Use<SaveRequest>().ExecuteAsync(p.Path, p.Request, ct),
                changed: (p, saved) => [p.Path, saved.Path],
                destructive: true)
            .Add<MoveRequestArguments, MovedRequestDto>(
                "move_request", "Move request",
                "Moves a request file into another folder of requests/. Returns its new path.",
                (p, _) => Task.FromResult(Use<MoveRequest>().Execute(p.Path, p.Folder)),
                changed: (p, moved) => [p.Path, moved.Path])
            .Add<PathArguments, Unit>(
                "delete_request", "Delete request",
                "Deletes a request file. Ask the user before deleting anything.",
                (p, _) => Task.FromResult(Use<DeleteRequest>().Execute(p.Path)),
                changed: (p, _) => [p.Path],
                destructive: true)
            .Add<CreateFolderArguments, FolderDto>(
                "create_folder", "Create folder",
                "Creates a folder inside requests/ (a collection of requests) or plans/ (a folder of run plans), or a folder in either. Returns its path.",
                (p, _) => Task.FromResult(Use<CreateFolder>().Execute(p.Parent, p.Name)),
                changed: (_, folder) => [folder.Path])
            .Add<SendArguments, ResponseDto>(
                "send_request", "Send request",
                "Sends an HTTP request with the selected environment and returns the response (status, headers, timings and body; "
                + $"bodies longer than {MaxBodyChars} characters are cut off and binary bodies are left out). Give the path of a saved request to send it as saved "
                + "(its response is kept in its history), or a request to send it unsaved. " + RequestShape,
                (p, ct) => Send(p, ct),
                changed: (p, _) => p.Path is { } path ? [path] : [],
                present: ForAssistant,
                openWorld: true)
            .Add<NoArguments, EnvironmentListDto>(
                "list_environments", "List environments",
                "Lists the workspace's environments and which one is selected (requests are sent with its variables).",
                (_, ct) => Use<ListEnvironments>().ExecuteAsync(ct),
                readOnly: true)
            .Add<EnvironmentArguments, EnvironmentDto>(
                "read_environment", "Read environment",
                "Reads an environment's variables. The values of secret variables are hidden.",
                (p, ct) => Use<LoadEnvironment>().ExecuteAsync(p.Id, ct),
                present: HideSecrets,
                readOnly: true)
            .Add<CreateEnvironmentArguments, SavedEnvironmentDto>(
                "create_environment", "Create environment",
                "Creates an environment: { name, variables: [{ name, value, secret }], cookieJar }. Secret values are kept on this computer only, "
                + "not in the file. Returns its id.",
                (p, ct) => Use<CreateEnvironment>().ExecuteAsync(p.Environment, ct),
                changed: (_, saved) => [$"{WorkspaceLayout.EnvironmentsFolder}/{saved.Id}"],
                present: saved => saved with { Environment = HideSecrets(saved.Environment) })
            .Add<SelectEnvironmentArguments, Unit>(
                "select_environment", "Select environment",
                "Selects the environment requests are sent with, by id (see list_environments); null selects none.",
                async (p, ct) => await Use<SelectEnvironment>().ExecuteAsync(p.Id, ct),
                changed: (_, _) => [])
            .Add<PathArguments, IReadOnlyList<HistoryEntryDto>>(
                "list_history", "List response history",
                "Lists the responses kept in a saved request's history, newest first: when it was sent, method, URL, status, time and size. "
                + "Responses are kept when a saved request is sent (if the user keeps history), with cookie and secret values hidden.",
                (p, ct) => Use<ListResponseHistory>().ExecuteAsync(p.Path, ct),
                readOnly: true)
            .Add<HistoryEntryArguments, HistoryEntryDetailsDto>(
                "get_history_entry", "Get response from history",
                "Reads one response of a saved request's history (its id from list_history): status, headers, timings and body; "
                + $"bodies longer than {MaxBodyChars} characters are cut off and binary bodies are left out.",
                (p, ct) => Use<GetResponseHistoryEntry>().ExecuteAsync(p.Path, p.Id, ct),
                present: entry => entry with { Response = ForAssistant(entry.Response) },
                readOnly: true)
            .Add<OpenArguments, WorkspaceItemDto>(
                "open_in_app", "Open in Missive",
                "Opens a request, run plan or environment file in a tab of the app, or shows its tab if it is open already, so the user sees it. "
                + "Use it to show the user what you created or changed.",
                (p, _) => Task.FromResult(Use<FindWorkspaceItem>().Execute(p.Path)),
                then: (_, item) => events.OpenRequestedAsync(item))
            .Add<NoArguments, IReadOnlyList<RunPlanFileDto>>(
                "list_run_plans", "List run plans",
                "Lists the workspace's load run plans (*.run.yaml files in plans/) with their names, and why any can't be read.",
                (_, ct) => Use<ListRunPlans>().ExecuteAsync(ct),
                readOnly: true)
            .Add<PlanPathArguments, RunPlanDocumentDto>(
                "read_run_plan", "Read run plan",
                "Reads a run plan's YAML, and why it can't be run yet, if it can't. " + RunPlanShape,
                (p, ct) => Use<OpenRunPlan>().ExecuteAsync(p.Path, ct),
                readOnly: true)
            .Add<CreateRunPlanArguments, RunPlanDocumentDto>(
                "create_run_plan", "Create run plan",
                "Saves a new run plan in a folder of plans/ (plans itself for the top level); the file is named after the plan and never replaces another one. "
                + "Returns its path and why it can't be run yet, if it can't. " + RunPlanShape,
                (p, ct) => Use<CreateRunPlan>().ExecuteAsync(p.Folder, p.Yaml, ct),
                changed: (_, saved) => [saved.Path])
            .Add<SaveRunPlanArguments, RunPlanDocumentDto>(
                "save_run_plan", "Save run plan",
                "Replaces an existing run plan's YAML (use create_run_plan for a new plan); a plan whose file is named after it is renamed with it. "
                + "Returns its (possibly new) path and why it can't be run yet, if it can't. Read an existing plan first and keep what you don't mean to change. "
                + RunPlanShape,
                (p, ct) => Use<SaveRunPlan>().ExecuteAsync(p.Path, p.Yaml, ct),
                changed: (p, saved) => [p.Path, saved.Path],
                destructive: true)
            .Add<MoveRunPlanArguments, MovedRunPlanDto>(
                "move_run_plan", "Move run plan",
                "Moves a run plan into another folder of plans/; its earlier runs follow it. Returns its new path.",
                (p, ct) => Use<MoveRunPlan>().ExecuteAsync(p.Path, p.Folder, ct),
                changed: (p, moved) => [p.Path, moved.Path])
            .Add<PlanPathArguments, Unit>(
                "delete_run_plan", "Delete run plan",
                "Deletes a run plan and its saved runs. Ask the user before deleting anything.",
                async (p, ct) => await Use<DeleteRunPlan>().ExecuteAsync(p.Path, ct),
                changed: (p, _) => [p.Path],
                destructive: true)
            .Add<StartRunArguments, StartedRunDto>(
                "start_run", "Start load run",
                "Starts a load run of a saved plan with the selected environment and returns its id at once; the run goes on in the background "
                + "and the user sees it in the app. Follow it with get_run. A load run sends many requests: only run plans against servers the user "
                + $"means to load test, and ask them before a large run (more than {Domain.Runs.RunPlan.WarnThreads} threads needs confirmLarge).",
                (p, ct) => Use<StartRun>().ExecuteAsync(p.Path, p.ConfirmLarge, ct),
                then: (p, started) => events.RunStartedAsync(p.Path, started.RunId),
                openWorld: true)
            .Add<GetRunArguments, RunResultDto>(
                "get_run", "Get load run",
                "Reports a run, live or finished: its state (running, completed, cancelled or failed), overall and per-request statistics "
                + "(latency percentiles in ms, throughput in requests per second, errors, status codes), thresholds with their verdicts, "
                + $"and the failed and slowest requests. Give waitSeconds (at most {(int)WaitForRun.MaxWait.TotalSeconds}) to wait for a running run to finish first. "
                + $"Only the last {MaxTimelinePoints} seconds of the per-second timeline are shown.",
                (p, ct) => Use<WaitForRun>().ExecuteAsync(p.RunId, TimeSpan.FromSeconds(Math.Max(0, p.WaitSeconds)), ct),
                present: ForAssistant,
                readOnly: true)
            .Add<RunIdArguments, Unit>(
                "cancel_run", "Cancel load run",
                "Stops a running load run; the results so far are kept.",
                (p, _) => Task.FromResult(Use<CancelRun>().Execute(p.RunId)))
            .Add<PlanPathArguments, IReadOnlyList<RunSummaryDto>>(
                "list_runs", "List load runs",
                "Lists the finished runs of a plan, newest first, with their headline numbers.",
                (p, ct) => Use<ListRuns>().ExecuteAsync(p.Path, ct),
                readOnly: true)
            .Add<RunSamplesArguments, RunSamplesPageDto>(
                "get_run_samples", "Get load run requests",
                $"Reads a page of a run's request log (the first {ActiveRun.MaxLoggedSamples} requests it sent, in the order they finished): "
                + $"time into the run, virtual user, iteration, URL, status, latency, error and the start of the response. At most {MaxSamplesPage} per page.",
                (p, ct) => Use<GetRunSamples>().ExecuteAsync(p.RunId, Math.Max(0, p.Offset), Math.Clamp(p.Limit, 1, MaxSamplesPage), p.FailedOnly, ct),
                readOnly: true)
            .Add<RunIdsArguments, Unit>(
                "delete_runs", "Delete load runs",
                "Deletes saved runs (their results, request logs and saved responses); the plans stay. Ask the user before deleting anything.",
                async (p, ct) => await Use<DeleteRun>().ExecuteAsync(p.RunIds, ct),
                changed: (_, _) => [],
                destructive: true)
            .Add<NoArguments, GitStatusDto>(
                "git_status", "Git status",
                "Shows the workspace's git branch and changed files. Missive leaves committing, pulling and pushing to the user.",
                async (_, ct) => (await Use<GetGitStatus>().ExecuteAsync(ct)).Map(GitStatusDto.From),
                readOnly: true)
            .Add<PathArguments, GitFileDiffDto>(
                "git_diff", "Git diff",
                "Shows how a changed file of the workspace (see git_status) differs from the last commit: its text before (original) and now (modified). "
                + $"Each side is cut off after {MaxBodyChars} characters.",
                (p, ct) => Use<GetFileDiff>().ExecuteAsync(p.Path, ct),
                present: ForAssistant,
                readOnly: true)
            .Add<GitLogArguments, GitHistoryDto>(
                "git_log", "Git log",
                "Lists the commits of the current branch, newest first: id, author, date and subject.",
                async (p, ct) => (await Use<GetGitHistory>().ExecuteAsync(new GetGitHistoryRequest(Math.Max(0, p.Skip), Math.Clamp(p.Limit, 1, MaxCommitsPage)), ct)).Map(GitHistoryDto.From),
                readOnly: true)
            .Add<CommitArguments, GitCommitDetailsDto>(
                "git_show", "Git show commit",
                "Shows one commit (its id from git_log): its whole message and the files it changed.",
                async (p, ct) => (await Use<GetCommit>().ExecuteAsync(p.CommitId, ct)).Map(GitCommitDetailsDto.From),
                readOnly: true)
            .Build();

        Task<Result<ResponseDto>> Send(SendArguments p, CancellationToken ct)
        {
            var id = $"mcp-{Guid.NewGuid():N}";
            return p switch
            {
                { Request: { } request } => Use<SendRequest>().ExecuteAsync(id, request, p.Path, ct),
                { Path: { } path } => Use<SendRequestFile>().ExecuteAsync(id, path, ct),
                _ => Task.FromResult<Result<ResponseDto>>(new Error(ErrorCodes.InvalidRequest, "Give the path of a saved request, or a request to send.")),
            };
        }
    }

    /// <summary>A response as the AI assistant sees it: long text bodies cut off and binary bodies left out.</summary>
    /// <summary>A run as the AI assistant sees it: only the latest seconds of its timeline.</summary>
    internal static RunResultDto ForAssistant(RunResultDto run) =>
        run.Timeline.Count > MaxTimelinePoints ? run with { Timeline = run.Timeline.TakeLast(MaxTimelinePoints).ToList() } : run;

    /// <summary>
    /// A diff as the AI assistant sees it: each side's text cut off when long, and without the
    /// parsed copies of request files, which repeat the text.
    /// </summary>
    internal static GitFileDiffDto ForAssistant(GitFileDiffDto diff) =>
        diff with
        {
            Original = Cut(diff.Original),
            Modified = Cut(diff.Modified),
            OriginalRequest = null,
            ModifiedRequest = null,
        };

    private static string? Cut(string? text) =>
        text is { Length: > MaxBodyChars } ? text[..MaxBodyChars] + "\n… (cut off)" : text;

    internal static ResponseDto ForAssistant(ResponseDto response) =>
        response switch
        {
            { BodyEncoding: "base64" } => response with { Body = $"(binary {response.ContentType ?? "body"} of {response.Size} bytes, not shown)", BodyEncoding = "omitted" },
            { Body.Length: > MaxBodyChars } => response with { Body = response.Body[..MaxBodyChars], Truncated = true },
            _ => response,
        };

    private static EnvironmentDto HideSecrets(EnvironmentDto environment) =>
        environment with
        {
            Variables = environment.Variables
                .Select(variable => variable.Secret ? variable with { Value = "(secret)" } : variable)
                .ToList(),
        };

    /// <summary>The JSON Schema of <typeparamref name="T"/>, as tool arguments.</summary>
    internal static JsonObject SchemaOf<T>()
    {
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(JsonOptions, typeof(T), new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                var attributes = context.PropertyInfo?.AttributeProvider ?? context.TypeInfo.Type;
                if (node is JsonObject obj && attributes.GetCustomAttributes(typeof(DescriptionAttribute), inherit: false) is [DescriptionAttribute description, ..])
                {
                    obj.Insert(0, "description", description.Description);
                }

                return node;
            },
        });
        return (JsonObject)InlineReferences(schema, schema);
    }

    /// <summary>
    /// Replaces the exporter's <c>$ref</c>s to repeated types with a copy of the schema they point
    /// to: not every MCP client follows references. The tools' arguments have no recursive types.
    /// </summary>
    private static JsonNode InlineReferences(JsonNode node, JsonNode root)
    {
        switch (node)
        {
            case JsonObject obj when obj["$ref"]?.GetValue<string>() is { } reference && reference.StartsWith("#/", StringComparison.Ordinal):
                var target = reference[2..].Split('/').Aggregate<string, JsonNode?>(root, (current, segment) => current?[segment.Replace("~1", "/").Replace("~0", "~")])
                    ?? throw new InvalidOperationException($"Unresolved schema reference '{reference}'.");
                return InlineReferences(target.DeepClone(), root);
            case JsonObject obj:
                foreach (var name in obj.Select(property => property.Key).ToList())
                {
                    if (obj[name] is { } child)
                    {
                        var inlined = InlineReferences(child, root);
                        if (!ReferenceEquals(inlined, child))
                        {
                            obj[name] = inlined;
                        }
                    }
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } item)
                    {
                        var inlined = InlineReferences(item, root);
                        if (!ReferenceEquals(inlined, item))
                        {
                            array[i] = inlined;
                        }
                    }
                }

                return array;
            default:
                return node;
        }
    }

    private sealed class Builder(HostEvents events)
    {
        private readonly List<McpTool> _tools = [];

        /// <param name="changed">For tools that change the workspace: the paths that changed, which the UI re-reads.</param>
        /// <param name="present">Turns the result into what the AI assistant is shown.</param>
        /// <param name="then">Runs after the use case succeeds, to tell the UI about it.</param>
        public Builder Add<TArguments, TResult>(
            string name,
            string title,
            string description,
            Func<TArguments, CancellationToken, Task<Result<TResult>>> handler,
            Func<TArguments, TResult, IReadOnlyList<string>>? changed = null,
            Func<TResult, TResult>? present = null,
            Func<TArguments, TResult, Task>? then = null,
            bool readOnly = false,
            bool destructive = false,
            bool openWorld = false)
        {
            _tools.Add(new McpTool(name, title, description, SchemaOf<TArguments>(), readOnly, destructive, openWorld, async (arguments, cancellationToken) =>
            {
                TArguments? typed;
                try
                {
                    typed = arguments.Deserialize<TArguments>(JsonOptions);
                }
                catch (JsonException ex)
                {
                    return new McpToolResult($"Invalid arguments for {name}: {ex.Message}", IsError: true);
                }

                if (typed is null)
                {
                    return new McpToolResult($"{name} needs arguments.", IsError: true);
                }

                var result = await handler(typed, cancellationToken);
                if (!result.IsSuccess)
                {
                    return new McpToolResult($"{result.Error!.Code}: {result.Error.Message}", IsError: true);
                }

                if (changed is not null)
                {
                    await events.WorkspaceChangedAsync(changed(typed, result.Value).Distinct(StringComparer.Ordinal).ToList());
                }

                if (then is not null)
                {
                    await then(typed, result.Value);
                }

                var value = present is null ? result.Value : present(result.Value);
                return new McpToolResult(value is Unit ? "Done." : JsonSerializer.Serialize(value, JsonOptions));
            }));
            return this;
        }

        public IReadOnlyList<McpTool> Build() => _tools;
    }
}

// The arguments of the tools. Descriptions end up in their JSON Schemas.

internal sealed record NoArguments;

internal sealed record PathArguments(
    [property: Description("The request's workspace path, such as requests/users/get-user.yaml.")] string Path);

internal sealed record CreateRequestArguments(
    [property: Description("The folder to save it in: requests, or a folder in it such as requests/users.")] string Folder,
    RequestDto Request);

internal sealed record SaveRequestArguments(
    [property: Description("The request's workspace path, such as requests/users/get-user.yaml.")] string Path,
    RequestDto Request);

internal sealed record MoveRequestArguments(
    [property: Description("The request's workspace path, such as requests/users/get-user.yaml.")] string Path,
    [property: Description("The folder to move it to: requests, or a folder in it.")] string Folder);

internal sealed record CreateFolderArguments(
    [property: Description("The folder to create it in: requests or plans, or a folder in one of them.")] string Parent,
    [property: Description("The new folder's name.")] string Name);

internal sealed record SendArguments(
    [property: Description("The workspace path of a saved request to send, or, with request, the saved request it is a changed copy of (for its history).")] string? Path = null,
    [property: Description("A request to send as given, instead of a saved one.")] RequestDto? Request = null);

internal sealed record EnvironmentArguments(
    [property: Description("The environment's id, from list_environments.")] string Id);

internal sealed record CreateEnvironmentArguments(EnvironmentDto Environment);

internal sealed record HistoryEntryArguments(
    [property: Description("The request's workspace path, such as requests/users/get-user.yaml.")] string Path,
    [property: Description("The entry's id, from list_history.")] string Id);

internal sealed record OpenArguments(
    [property: Description("The workspace path of a request (requests/…/x.yaml), run plan (plans/…/x.run.yaml) or environment (environments/x.yaml).")] string Path);

internal sealed record GitLogArguments(
    [property: Description("How many commits to skip, newest first.")] int Skip = 0,
    [property: Description("How many commits to list (at most 100).")] int Limit = 20);

internal sealed record CommitArguments(
    [property: Description("The commit's id, from git_log.")] string CommitId);

internal sealed record PlanPathArguments(
    [property: Description("The plan's workspace path, such as plans/perf/checkout-smoke.run.yaml.")] string Path);

internal sealed record CreateRunPlanArguments(
    [property: Description("The folder to save it in: plans, or a folder in it such as plans/perf.")] string Folder,
    [property: Description("The plan's whole YAML text.")] string Yaml);

internal sealed record SaveRunPlanArguments(
    [property: Description("The plan's workspace path, such as plans/perf/checkout-smoke.run.yaml.")] string Path,
    [property: Description("The plan's whole YAML text.")] string Yaml);

internal sealed record MoveRunPlanArguments(
    [property: Description("The plan's workspace path, such as plans/perf/checkout-smoke.run.yaml.")] string Path,
    [property: Description("The folder to move it to: plans, or a folder in it.")] string Folder);

internal sealed record StartRunArguments(
    [property: Description("The plan's workspace path, such as plans/perf/checkout-smoke.run.yaml.")] string Path,
    [property: Description("Confirms a run of more than 200 threads; ask the user first.")] bool ConfirmLarge = false);

internal sealed record RunIdArguments(
    [property: Description("The run's id, from start_run or list_runs.")] string RunId);

internal sealed record GetRunArguments(
    [property: Description("The run's id, from start_run or list_runs.")] string RunId,
    [property: Description("Seconds to wait for a running run to finish before reporting it (at most 50); 0 reports at once.")] int WaitSeconds = 0);

internal sealed record RunSamplesArguments(
    [property: Description("The run's id, from start_run or list_runs.")] string RunId,
    [property: Description("How many logged requests to skip.")] int Offset = 0,
    [property: Description("How many logged requests to return (at most 100).")] int Limit = 50,
    [property: Description("Return only the failed requests.")] bool FailedOnly = false);

internal sealed record RunIdsArguments(
    [property: Description("The ids of the runs to delete, from list_runs.")] IReadOnlyList<string> RunIds);

internal sealed record SelectEnvironmentArguments(
    [property: Description("The environment's id, from list_environments; null for no environment.")] string? Id);
