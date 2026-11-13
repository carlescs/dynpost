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
using Missive.Application.Git;
using Missive.Application.Http;
using Missive.Application.Requests;
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
                "Creates a collection folder inside a folder of requests/ (requests itself for the top level). Returns its path.",
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
            .Add<NoArguments, GitStatusDto>(
                "git_status", "Git status",
                "Shows the workspace's git branch and changed files. Missive leaves committing, pulling and pushing to the user.",
                async (_, ct) => (await Use<GetGitStatus>().ExecuteAsync(ct)).Map(GitStatusDto.From),
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
        public Builder Add<TArguments, TResult>(
            string name,
            string title,
            string description,
            Func<TArguments, CancellationToken, Task<Result<TResult>>> handler,
            Func<TArguments, TResult, IReadOnlyList<string>>? changed = null,
            Func<TResult, TResult>? present = null,
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
    [property: Description("The folder to create it in: requests, or a folder in it.")] string Parent,
    [property: Description("The new folder's name.")] string Name);

internal sealed record SendArguments(
    [property: Description("The workspace path of a saved request to send, or, with request, the saved request it is a changed copy of (for its history).")] string? Path = null,
    [property: Description("A request to send as given, instead of a saved one.")] RequestDto? Request = null);

internal sealed record EnvironmentArguments(
    [property: Description("The environment's id, from list_environments.")] string Id);

internal sealed record CreateEnvironmentArguments(EnvironmentDto Environment);

internal sealed record SelectEnvironmentArguments(
    [property: Description("The environment's id, from list_environments; null for no environment.")] string? Id);
