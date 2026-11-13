namespace Dynypost.Application.Mcp;

/// <summary>The MCP server's settings and state, as the UI shows them.</summary>
/// <param name="Enabled">Whether the user turned the server on.</param>
/// <param name="Running">Whether it is listening.</param>
/// <param name="Url">The URL clients connect to.</param>
/// <param name="Token">The bearer token clients must send; <c>null</c> until the server is first turned on.</param>
/// <param name="Error">Why the server could not start, if it couldn't.</param>
public sealed record McpStatusDto(bool Enabled, bool Running, int Port, string Url, string? Token, string? Error = null)
{
    public static McpStatusDto From(McpSettings settings, IMcpServer server) =>
        new(
            settings.Enabled,
            server.IsRunning,
            settings.Port,
            McpSettings.UrlFor(settings.Port),
            settings.Token,
            settings.Enabled && !server.IsRunning ? server.Error : null);
}
