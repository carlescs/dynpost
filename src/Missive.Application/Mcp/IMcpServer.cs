using Missive.Application.Common;

namespace Missive.Application.Mcp;

/// <summary>The local MCP server that AI assistants connect to.</summary>
public interface IMcpServer
{
    /// <summary>Whether the server is listening.</summary>
    bool IsRunning { get; }

    /// <summary>Why the server could not start the last time it tried (for example, the port is in use); <c>null</c> otherwise.</summary>
    string? Error { get; }

    /// <summary>Starts listening on <paramref name="port"/> of this computer only, accepting clients that send <paramref name="token"/>; restarts it if it was running.</summary>
    Task<Result<Unit>> StartAsync(int port, string token, CancellationToken cancellationToken = default);

    /// <summary>Stops listening; does nothing if the server isn't running.</summary>
    Task StopAsync();
}
