using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dynypost.Desktop.Mcp;

/// <summary>
/// The Model Context Protocol's JSON-RPC messages, as far as a server that only offers tools needs
/// them: <c>initialize</c>, <c>ping</c>, <c>tools/list</c> and <c>tools/call</c>. Transport-neutral:
/// <see cref="McpHttpServer"/> feeds it the messages it receives.
/// </summary>
internal sealed class McpProtocol(IReadOnlyList<McpTool> tools, string version)
{
    /// <summary>The protocol versions understood, the newest first.</summary>
    public static readonly IReadOnlyList<string> ProtocolVersions = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    private const string Instructions =
        "Dynypost is a desktop HTTP client whose requests are YAML files in a workspace folder, usually a git repository. "
        + "These tools act on the workspace open in the app, and the user sees their changes there. "
        + "Paths are relative to the workspace folder: requests and their collection folders are in requests/. "
        + "Values may use {{variable}} references, resolved with the selected environment when a request is sent. "
        + "Read a request before changing it, and ask the user before deleting anything.";

    private readonly Dictionary<string, McpTool> _tools = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

    /// <summary>
    /// Handles one HTTP request body: a JSON-RPC message or a batch of them. Returns the response
    /// to send, or <c>null</c> when there is none (the body held only notifications or responses).
    /// </summary>
    public async Task<JsonNode?> HandleAsync(string body, CancellationToken cancellationToken = default)
    {
        JsonElement message;
        try
        {
            message = JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException ex)
        {
            return ErrorResponse(null, ParseError, $"Invalid JSON: {ex.Message}");
        }

        if (message.ValueKind != JsonValueKind.Array)
        {
            return await HandleMessageAsync(message, cancellationToken);
        }

        var responses = new JsonArray();
        foreach (var item in message.EnumerateArray())
        {
            if (await HandleMessageAsync(item, cancellationToken) is { } response)
            {
                responses.Add(response);
            }
        }

        return responses.Count > 0 ? responses : null;
    }

    private async Task<JsonNode?> HandleMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            return ErrorResponse(null, InvalidRequest, "A JSON-RPC message must be an object.");
        }

        var id = message.TryGetProperty("id", out var idElement) && idElement.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? JsonNode.Parse(idElement.GetRawText())
            : null;
        if (!message.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
        {
            // A response to a request of the server's (it sends none), or garbage: nothing to answer.
            return id is null || message.TryGetProperty("result", out _) || message.TryGetProperty("error", out _)
                ? null
                : ErrorResponse(id, InvalidRequest, "The message has no method.");
        }

        var method = methodElement.GetString()!;
        var parameters = message.TryGetProperty("params", out var paramsElement) && paramsElement.ValueKind == JsonValueKind.Object
            ? paramsElement
            : default;

        // Notifications (initialized, cancelled, ...) need no answer, and none of them changes anything here.
        if (id is null)
        {
            return null;
        }

        try
        {
            return method switch
            {
                "initialize" => Result(id, Initialize(parameters)),
                "ping" => Result(id, new JsonObject()),
                "tools/list" => Result(id, ListTools()),
                "tools/call" => await CallToolAsync(id, parameters, cancellationToken),
                _ => ErrorResponse(id, MethodNotFound, $"Unknown method '{method}'."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"MCP method '{method}' failed: {ex}");
            return ErrorResponse(id, InternalError, ex.Message);
        }
    }

    private JsonObject Initialize(JsonElement parameters)
    {
        var requested = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("protocolVersion", out var versionElement)
            && versionElement.ValueKind == JsonValueKind.String
                ? versionElement.GetString()
                : null;
        return new JsonObject
        {
            ["protocolVersion"] = ProtocolVersions.Contains(requested) ? requested : ProtocolVersions[0],
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "dynypost", ["title"] = "Dynypost", ["version"] = version },
            ["instructions"] = Instructions,
        };
    }

    private JsonObject ListTools() =>
        new()
        {
            ["tools"] = new JsonArray(tools.Select(tool => (JsonNode)new JsonObject
            {
                ["name"] = tool.Name,
                ["title"] = tool.Title,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema.DeepClone(),
                ["annotations"] = new JsonObject
                {
                    ["title"] = tool.Title,
                    ["readOnlyHint"] = tool.ReadOnly,
                    ["destructiveHint"] = tool.Destructive,
                    ["idempotentHint"] = tool.ReadOnly,
                    ["openWorldHint"] = tool.OpenWorld,
                },
            }).ToArray()),
        };

    private async Task<JsonNode> CallToolAsync(JsonNode id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
        {
            return ErrorResponse(id, InvalidParams, "tools/call needs the name of a tool.");
        }

        var name = nameElement.GetString()!;
        if (!_tools.TryGetValue(name, out var tool))
        {
            return ErrorResponse(id, InvalidParams, $"Unknown tool '{name}'.");
        }

        var arguments = parameters.TryGetProperty("arguments", out var argumentsElement) && argumentsElement.ValueKind == JsonValueKind.Object
            ? argumentsElement
            : JsonDocument.Parse("{}").RootElement;

        McpToolResult result;
        try
        {
            result = await tool.Call(arguments, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"MCP tool '{name}' failed: {ex}");
            result = new McpToolResult($"internal_error: {ex.Message}", IsError: true);
        }

        return Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Text }),
            ["isError"] = result.IsError,
        });
    }

    private static JsonObject Result(JsonNode id, JsonNode result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    public static JsonObject ErrorResponse(JsonNode? id, int code, string message) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };
}
