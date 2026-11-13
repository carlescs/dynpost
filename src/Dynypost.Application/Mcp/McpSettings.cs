namespace Dynypost.Application.Mcp;

/// <summary>
/// The MCP server that lets AI assistants control the app, as the user set it up. Kept in the
/// per-user <see cref="Settings.AppSettings"/>, never in a workspace.
/// </summary>
/// <param name="Enabled">Whether the server runs while the app is open; off unless the user turns it on.</param>
/// <param name="Port">The local port it listens on.</param>
/// <param name="Token">The secret clients send as a bearer token; made when the server is first turned on.</param>
public sealed record McpSettings(bool Enabled = false, int Port = McpSettings.DefaultPort, string? Token = null)
{
    public const int DefaultPort = 47823;

    /// <summary>The lowest port accepted: lower ones are reserved for system services.</summary>
    public const int MinPort = 1024;

    public const int MaxPort = 65535;

    /// <summary>The path the server answers MCP requests on.</summary>
    public const string EndpointPath = "/mcp";

    public static McpSettings Default { get; } = new();

    public static bool IsValidPort(int port) => port is >= MinPort and <= MaxPort;

    /// <summary>The URL clients connect to when the server listens on <paramref name="port"/>.</summary>
    public static string UrlFor(int port) => $"http://localhost:{port}{EndpointPath}";
}
