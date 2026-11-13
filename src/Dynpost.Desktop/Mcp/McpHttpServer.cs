using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Dynpost.Application.About;
using Dynpost.Application.Common;
using Dynpost.Application.Mcp;
using Dynpost.Desktop.Bridge;

namespace Dynpost.Desktop.Mcp;

/// <summary>
/// Serves the MCP tools (<see cref="McpTools"/>) over the protocol's Streamable HTTP transport, on
/// <c>http://localhost:{port}/mcp</c>. It answers each POSTed message with plain JSON (it sends
/// no server-initiated messages, so it opens no event streams) and keeps no sessions.
/// </summary>
/// <remarks>
/// Only this computer can connect: the listener is bound to <c>localhost</c>. Clients must send the
/// token as <c>Authorization: Bearer …</c>, and requests from web pages (with an <c>Origin</c>
/// other than localhost) are refused, so a website can't drive the app through the user's browser.
/// </remarks>
internal sealed class McpHttpServer(IServiceProvider services, HostEvents events, IAppInfo appInfo) : IMcpServer, IDisposable
{
    /// <summary>The largest request body accepted; tool arguments are small.</summary>
    public const int MaxBodyBytes = 4 * 1024 * 1024;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private McpProtocol? _protocol;
    private HttpListener? _listener;
    private CancellationTokenSource? _stopping;
    private Task _loop = Task.CompletedTask;
    private byte[] _token = [];

    public bool IsRunning => _listener?.IsListening == true;

    public string? Error { get; private set; }

    public async Task<Result<Unit>> StartAsync(int port, string token, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await StopUnlockedAsync();
            _protocol ??= new McpProtocol(McpTools.Create(services, events), appInfo.Version);
            _token = Encoding.UTF8.GetBytes(token);

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
            }
            catch (Exception ex) when (ex is HttpListenerException or System.Net.Sockets.SocketException or InvalidOperationException)
            {
                listener.Close();
                Error = $"Could not listen on port {port}: {ex.Message}";
                return new Error("mcp.start_failed", Error);
            }

            Error = null;
            _listener = listener;
            _stopping = new CancellationTokenSource();
            _loop = Task.Run(() => AcceptAsync(listener, _protocol, _stopping.Token), CancellationToken.None);
            return Unit.Value;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await StopUnlockedAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _lock.Dispose();
    }

    private async Task StopUnlockedAsync()
    {
        if (_listener is not { } listener)
        {
            return;
        }

        _listener = null;
        _stopping!.Cancel();
        listener.Close();
        try
        {
            await _loop;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"MCP server stopped with an error: {ex}");
        }

        _stopping.Dispose();
        _stopping = null;
    }

    private async Task AcceptAsync(HttpListener listener, McpProtocol protocol, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                // Closed by StopAsync.
                return;
            }

            _ = Task.Run(() => HandleAsync(context, protocol, stopping), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, McpProtocol protocol, CancellationToken stopping)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            if (!string.Equals(request.Url?.AbsolutePath.TrimEnd('/'), McpSettings.EndpointPath, StringComparison.Ordinal))
            {
                await RespondAsync(response, HttpStatusCode.NotFound, "Not found. The MCP endpoint is " + McpSettings.EndpointPath + ".");
                return;
            }

            if (!IsAllowedOrigin(request.Headers["Origin"]))
            {
                await RespondAsync(response, HttpStatusCode.Forbidden, "Requests from web pages are not accepted.");
                return;
            }

            if (!IsAuthorized(request.Headers["Authorization"]))
            {
                response.AddHeader("WWW-Authenticate", "Bearer");
                await RespondAsync(response, HttpStatusCode.Unauthorized, "Send the token shown in Dynpost's AI assistant settings as a bearer token.");
                return;
            }

            if (request.HttpMethod != "POST")
            {
                // No server-initiated messages (GET would open a stream for them) and no sessions to end (DELETE).
                response.AddHeader("Allow", "POST");
                await RespondAsync(response, HttpStatusCode.MethodNotAllowed, "Use POST.");
                return;
            }

            if (request.ContentLength64 > MaxBodyBytes)
            {
                await RespondAsync(response, HttpStatusCode.RequestEntityTooLarge, "The request is too large.");
                return;
            }

            string body;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
            {
                body = await ReadLimitedAsync(reader, stopping);
            }

            var answer = await protocol.HandleAsync(body, stopping);
            if (answer is null)
            {
                response.StatusCode = (int)HttpStatusCode.Accepted;
                response.Close();
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(answer.ToJsonString(McpTools.JsonOptions));
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, stopping);
            response.Close();
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                Trace.TraceError($"MCP request failed: {ex}");
            }

            try
            {
                response.Abort();
            }
            catch (Exception)
            {
                // The connection is gone already.
            }
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[8192];
        var text = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > MaxBodyBytes)
            {
                throw new InvalidDataException("The request is too large.");
            }
        }

        return text.ToString();
    }

    private bool IsAuthorized(string? header)
    {
        const string scheme = "Bearer ";
        if (header is null || !header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sent = Encoding.UTF8.GetBytes(header[scheme.Length..].Trim());
        return _token.Length > 0 && CryptographicOperations.FixedTimeEquals(sent, _token);
    }

    /// <summary>Clients other than browsers send no <c>Origin</c>; pages may only come from this computer.</summary>
    internal static bool IsAllowedOrigin(string? origin) =>
        string.IsNullOrEmpty(origin)
        || (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Host is "localhost" or "127.0.0.1" or "[::1]");

    private static async Task RespondAsync(HttpListenerResponse response, HttpStatusCode status, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        response.StatusCode = (int)status;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}
