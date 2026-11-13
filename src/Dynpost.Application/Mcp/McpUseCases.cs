using System.Security.Cryptography;
using Dynpost.Application.Common;
using Dynpost.Application.Settings;

namespace Dynpost.Application.Mcp;

/// <summary>Reads the MCP server's settings and whether it is running.</summary>
public sealed class GetMcpStatus(IAppSettingsStore store, IMcpServer server)
{
    public async Task<Result<McpStatusDto>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        McpStatusDto.From(Settings(await store.LoadAsync(cancellationToken)), server);

    internal static McpSettings Settings(AppSettings settings) => settings.Mcp ?? McpSettings.Default;
}

/// <summary>
/// Turns the MCP server on or off, or moves it to another port, and remembers the choice. The
/// first time it is turned on it gets a random token that clients must send. A server that can't
/// start (for example, because the port is in use) stays turned on, with the reason in the status,
/// so the user can pick another port.
/// </summary>
public sealed class SetMcpServer(IAppSettingsStore store, IMcpServer server)
{
    public const string InvalidPortCode = "mcp.invalid_port";

    /// <param name="port">The port to listen on; <c>null</c> keeps the current one.</param>
    public async Task<Result<McpStatusDto>> ExecuteAsync(bool enabled, int? port = null, CancellationToken cancellationToken = default)
    {
        if (port is { } requested && !McpSettings.IsValidPort(requested))
        {
            return new Error(InvalidPortCode, $"Choose a port between {McpSettings.MinPort} and {McpSettings.MaxPort}.");
        }

        var saved = await store.UpdateAsync(
            settings =>
            {
                var current = GetMcpStatus.Settings(settings);
                var updated = current with
                {
                    Enabled = enabled,
                    Port = port ?? current.Port,
                    Token = current.Token ?? (enabled ? McpTokens.Create() : null),
                };
                return updated == current ? settings : settings with { Mcp = updated };
            },
            cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error!;
        }

        var mcp = GetMcpStatus.Settings(saved.Value);
        await McpTokens.ApplyAsync(server, mcp, cancellationToken);
        return McpStatusDto.From(mcp, server);
    }
}

/// <summary>
/// Replaces the MCP server's token, so clients given the old one can no longer connect. A running
/// server is restarted to use the new token.
/// </summary>
public sealed class RegenerateMcpToken(IAppSettingsStore store, IMcpServer server)
{
    public async Task<Result<McpStatusDto>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var saved = await store.UpdateAsync(
            settings => settings with { Mcp = GetMcpStatus.Settings(settings) with { Token = McpTokens.Create() } },
            cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error!;
        }

        var mcp = GetMcpStatus.Settings(saved.Value);
        if (mcp.Enabled)
        {
            await McpTokens.ApplyAsync(server, mcp, cancellationToken);
        }

        return McpStatusDto.From(mcp, server);
    }
}

/// <summary>Starts the MCP server when the app starts, if the user turned it on.</summary>
public sealed class StartMcpServer(IAppSettingsStore store, IMcpServer server)
{
    public async Task<Result<McpStatusDto>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var mcp = GetMcpStatus.Settings(await store.LoadAsync(cancellationToken));
        await McpTokens.ApplyAsync(server, mcp, cancellationToken);
        return McpStatusDto.From(mcp, server);
    }
}

internal static class McpTokens
{
    /// <summary>A new random token: 32 bytes, as lower-case hex.</summary>
    public static string Create() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    /// <summary>Starts (or restarts) the server as <paramref name="settings"/> say, or stops it. A failure to start is kept in <see cref="IMcpServer.Error"/>.</summary>
    public static async Task ApplyAsync(IMcpServer server, McpSettings settings, CancellationToken cancellationToken)
    {
        if (settings is { Enabled: true, Token: { } token })
        {
            await server.StartAsync(settings.Port, token, cancellationToken);
        }
        else
        {
            await server.StopAsync();
        }
    }
}
