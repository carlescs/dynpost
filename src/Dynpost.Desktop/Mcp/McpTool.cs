using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dynpost.Desktop.Mcp;

/// <summary>A tool the MCP server offers AI assistants.</summary>
/// <param name="Name">The tool's name, as clients call it (<c>read_request</c>).</param>
/// <param name="Title">A short, human-readable name.</param>
/// <param name="Description">What the tool does and how to use it, for the AI assistant.</param>
/// <param name="InputSchema">The JSON Schema of the tool's arguments.</param>
/// <param name="ReadOnly">Whether the tool only reads (it changes no files or state).</param>
/// <param name="Destructive">Whether the tool can remove or overwrite the user's data.</param>
/// <param name="OpenWorld">Whether the tool reaches beyond the workspace (it sends HTTP requests).</param>
/// <param name="Call">Runs the tool with its arguments (a JSON object).</param>
internal sealed record McpTool(
    string Name,
    string Title,
    string Description,
    JsonObject InputSchema,
    bool ReadOnly,
    bool Destructive,
    bool OpenWorld,
    Func<JsonElement, CancellationToken, Task<McpToolResult>> Call);

/// <summary>What a tool call returned: text for the AI assistant, and whether it is an error.</summary>
internal sealed record McpToolResult(string Text, bool IsError = false);
