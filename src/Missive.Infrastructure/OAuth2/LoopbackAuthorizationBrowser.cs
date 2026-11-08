using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Missive.Application.Common;
using Missive.Application.OAuth2;
using Missive.Domain.Http;

namespace Missive.Infrastructure.OAuth2;

/// <summary>
/// Opens the authorization URL in the system browser and receives the redirect on a loopback
/// address (RFC 8252 §7.3). It listens with a plain TCP socket on the loopback interface only, so
/// it needs no admin rights or URL reservations on any system and is never reachable from the
/// network. Only the first request to the redirect path is used; anything else (a favicon, …)
/// gets a 404.
/// </summary>
public sealed class LoopbackAuthorizationBrowser : IAuthorizationBrowser
{
    /// <summary>How long to wait for the user to sign in.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private const int MaxRequestHeadBytes = 16 * 1024;

    private readonly Action<Uri> _openBrowser;
    private readonly TimeSpan _timeout;
    private readonly Lock _gate = new();
    private Pending? _pending;

    /// <summary>The authorization waiting for its redirect, and whether it was stopped on purpose.</summary>
    private sealed class Pending(CancellationTokenSource source)
    {
        public CancellationTokenSource Source { get; } = source;

        public bool Stopped { get; private set; }

        public void Stop()
        {
            Stopped = true;
            Source.Cancel();
        }
    }

    /// <param name="openBrowser">Opens a URL; the system browser by default (replaced in tests).</param>
    /// <param name="timeout">How long to wait for the redirect; <see cref="DefaultTimeout"/> by default.</param>
    public LoopbackAuthorizationBrowser(Action<Uri>? openBrowser = null, TimeSpan? timeout = null)
    {
        _openBrowser = openBrowser ?? OpenInSystemBrowser;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<Result<AuthorizationCallback>> AuthorizeAsync(
        Uri? redirectUri,
        Func<Uri, Uri> authorizationUrl,
        CancellationToken cancellationToken = default)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new Pending(source);
        lock (_gate)
        {
            _pending?.Stop();
            _pending = pending;
        }

        var listeners = new List<TcpListener>();
        try
        {
            var redirect = Listen(redirectUri, listeners);
            if (!redirect.IsSuccess)
            {
                return redirect.Error!;
            }

            try
            {
                _openBrowser(authorizationUrl(redirect.Value));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                return OAuth2Errors.Failed($"Could not open the browser to sign in: {ex.Message}");
            }

            source.CancelAfter(_timeout);
            try
            {
                var parameters = await WaitForRedirectAsync(listeners, redirect.Value.AbsolutePath, source.Token);
                return new AuthorizationCallback(redirect.Value, parameters);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                bool stopped;
                lock (_gate)
                {
                    stopped = pending.Stopped;
                }

                return OAuth2Errors.Failed(stopped
                    ? "The sign-in was cancelled."
                    : $"No answer from the sign-in page within {_timeout.TotalMinutes:0} minutes.");
            }
        }
        finally
        {
            foreach (var listener in listeners)
            {
                listener.Stop();
            }

            lock (_gate)
            {
                if (_pending == pending)
                {
                    _pending = null;
                }
            }
        }
    }

    public bool CancelPending()
    {
        lock (_gate)
        {
            if (_pending is null)
            {
                return false;
            }

            _pending.Stop();
            _pending = null;
            return true;
        }
    }

    /// <summary>
    /// Starts listening for the redirect and returns the redirect URL. <c>localhost</c> listens on
    /// both the IPv4 and IPv6 loopback addresses, as browsers may resolve it to either.
    /// </summary>
    private static Result<Uri> Listen(Uri? redirectUri, List<TcpListener> listeners)
    {
        try
        {
            if (redirectUri is null)
            {
                var listener = Start(IPAddress.Loopback, 0, listeners);
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                return new Uri($"http://127.0.0.1:{port}{OAuth2Flow.DefaultRedirectPath}");
            }

            // An address (127.0.0.2, [::1]…) is listened on exactly; validation made sure it is loopback.
            var addresses = redirectUri.HostNameType switch
            {
                UriHostNameType.IPv4 or UriHostNameType.IPv6 => [IPAddress.Parse(redirectUri.DnsSafeHost)],
                _ => Socket.OSSupportsIPv6 ? new[] { IPAddress.Loopback, IPAddress.IPv6Loopback } : [IPAddress.Loopback],
            };
            foreach (var address in addresses)
            {
                try
                {
                    Start(address, redirectUri.Port, listeners);
                }
                catch (SocketException) when (address.Equals(IPAddress.IPv6Loopback) && listeners.Count > 0)
                {
                    // IPv6 unavailable for localhost: IPv4 alone will do.
                }
            }

            return redirectUri;
        }
        catch (SocketException ex)
        {
            return OAuth2Errors.Failed(redirectUri is null
                ? $"Could not listen for the sign-in answer: {ex.Message}"
                : $"Could not listen on {redirectUri.Authority} for the sign-in answer (is another program using port {redirectUri.Port}?): {ex.Message}");
        }
    }

    private static TcpListener Start(IPAddress address, int port, List<TcpListener> listeners)
    {
        var listener = new TcpListener(address, port);
        listener.Start();
        listeners.Add(listener);
        return listener;
    }

    private static async Task<IReadOnlyDictionary<string, string>> WaitForRedirectAsync(
        List<TcpListener> listeners,
        string path,
        CancellationToken cancellationToken)
    {
        var accepts = listeners.ToDictionary(l => l.AcceptTcpClientAsync(cancellationToken).AsTask(), l => l);
        while (true)
        {
            var done = await Task.WhenAny(accepts.Keys);
            var listener = accepts[done];
            accepts.Remove(done);
            using var client = await done;
            accepts[listener.AcceptTcpClientAsync(cancellationToken).AsTask()] = listener;

            var parameters = await HandleAsync(client, path, cancellationToken);
            if (parameters is not null)
            {
                return parameters;
            }
        }
    }

    /// <summary>Answers one connection; returns the query parameters when it was the redirect.</summary>
    private static async Task<IReadOnlyDictionary<string, string>?> HandleAsync(TcpClient client, string path, CancellationToken cancellationToken)
    {
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        reading.CancelAfter(TimeSpan.FromSeconds(10));
        var stream = client.GetStream();
        string? target;
        try
        {
            target = await ReadTargetAsync(stream, reading.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var queryStart = target?.IndexOf('?') ?? -1;
        var targetPath = target is null ? null : queryStart < 0 ? target : target[..queryStart];
        if (targetPath != path)
        {
            await RespondAsync(stream, "404 Not Found", "Not found.", cancellationToken);
            return null;
        }

        var parameters = ParseQuery(queryStart < 0 ? string.Empty : target![(queryStart + 1)..]);
        var message = parameters.TryGetValue("error", out var error)
            ? $"Sign-in failed: {error}. You can close this tab and return to Missive."
            : "Missive received the sign-in answer. You can close this tab and return to Missive.";
        await RespondAsync(stream, "200 OK", message, cancellationToken);
        return parameters;
    }

    /// <summary>Reads the request head and returns the target of a GET request line, or <c>null</c>.</summary>
    private static async Task<string?> ReadTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestHeadBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        var head = Encoding.ASCII.GetString(buffer, 0, length);
        var lineEnd = head.IndexOf("\r\n", StringComparison.Ordinal);
        var parts = (lineEnd < 0 ? head : head[..lineEnd]).Split(' ');
        return parts.Length == 3 && parts[0] == "GET" && parts[1].StartsWith('/') ? parts[1] : null;
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string message, CancellationToken cancellationToken)
    {
        var html = $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><title>Missive</title></head>
            <body style="font-family: system-ui, sans-serif; margin: 3rem;"><p>{WebUtility.HtmlEncode(message)}</p></body></html>
            """;
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        try
        {
            await stream.WriteAsync(head, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
        }
        catch (IOException)
        {
            // The browser went away; the parameters are what matters.
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = WebUtility.UrlDecode(equals < 0 ? pair : pair[..equals]);
            parameters.TryAdd(name, equals < 0 ? string.Empty : WebUtility.UrlDecode(pair[(equals + 1)..]));
        }

        return parameters;
    }

    private static void OpenInSystemBrowser(Uri url)
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }
            : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { ArgumentList = { url.AbsoluteUri } };
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("No program could be started to open the URL.");
    }
}
