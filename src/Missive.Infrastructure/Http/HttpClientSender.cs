using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Missive.Application.Common;
using Missive.Application.Http;
using Missive.Domain.Http;

namespace Missive.Infrastructure.Http;

/// <summary>
/// Sends requests with <see cref="HttpClient"/>. Responses are decompressed and redirects are
/// followed here rather than by the handler, so each hop can be reported. Cookies are only kept
/// when the request comes with a cookie jar. By default all sends share one pooled connection
/// handler, so connections are kept alive and reused (opening one per send and closing it leaves
/// its port in TIME_WAIT, and a load run then runs out of ephemeral ports); the handler measures
/// DNS, TCP connect and TLS times for the send that opens a connection. A client given to the
/// constructor (in tests) has no connection timings.
/// </summary>
public sealed class HttpClientSender : IHttpSender, IDisposable
{
    /// <summary>The User-Agent sent when the request doesn't set one.</summary>
    public const string DefaultUserAgent = "Missive";

    /// <summary>Bodies larger than this are cut off.</summary>
    public const int DefaultMaxBodyBytes = 20 * 1024 * 1024;

    /// <summary>Redirects beyond this many are not followed: the last redirect response is returned.</summary>
    public const int MaxRedirects = 20;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long a pooled connection is used before it's replaced, so DNS changes are picked up.</summary>
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a pooled connection may sit idle before it's closed. Kept short: servers, proxies
    /// and firewalls drop idle connections, and a request sent over one that is already gone fails
    /// with a reset ("An existing connection was forcibly closed by the remote host").
    /// </summary>
    private static readonly TimeSpan PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How many times a request that failed on a reused connection is sent again. Each failure
    /// drops that connection from the pool, so a few retries also get past several stale ones.
    /// </summary>
    public const int MaxStaleConnectionRetries = 3;

    /// <summary>Carries a send's timings to the handler callbacks of the connection it opens.</summary>
    private static readonly HttpRequestOptionsKey<ConnectionTimings> TimingsKey = new("Missive.ConnectionTimings");

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly TimeSpan _timeout;
    private readonly int _maxBodyBytes;

    /// <summary>Sends requests through a shared, pooled handler of its own, with connection timings.</summary>
    public HttpClientSender()
    {
        _client = new HttpClient(CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        _ownsClient = true;
        _timeout = DefaultTimeout;
        _maxBodyBytes = DefaultMaxBodyBytes;
    }

    /// <summary>Uses the given client, e.g. one with a test handler. Its own timeout is ignored.</summary>
    public HttpClientSender(HttpClient client, bool ownsClient = false, TimeSpan? timeout = null, int maxBodyBytes = DefaultMaxBodyBytes)
    {
        _client = client;
        _client.Timeout = Timeout.InfiniteTimeSpan;
        _ownsClient = ownsClient;
        _timeout = timeout ?? DefaultTimeout;
        _maxBodyBytes = maxBodyBytes;
    }

    /// <summary>
    /// A pooled handler that doesn't follow redirects or keep cookies itself and reports the
    /// phases of each new connection to the timings of the request that opened it.
    /// </summary>
    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(30),
        PooledConnectionLifetime = PooledConnectionLifetime,
        PooledConnectionIdleTimeout = PooledConnectionIdleTimeout,
        ConnectCallback = (context, cancellationToken) =>
            ConnectAsync(context.DnsEndPoint, TimingsOf(context.InitialRequestMessage), cancellationToken),
        // Called once the connection is ready to carry requests: after the TLS handshake for https.
        PlaintextStreamFilter = (context, _) =>
        {
            if (context.InitialRequestMessage.RequestUri?.Scheme == Uri.UriSchemeHttps)
            {
                TimingsOf(context.InitialRequestMessage)?.HandshakeDone();
            }

            return ValueTask.FromResult(context.PlaintextStream);
        },
    };

    private static ConnectionTimings? TimingsOf(HttpRequestMessage message) =>
        message.Options.TryGetValue(TimingsKey, out var timings) ? timings : null;

    public async Task<Result<HttpResponseData>> SendAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
    {
        var timings = new ConnectionTimings();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        var jar = request.Cookies is null ? null : ToContainer(request.Cookies);
        var method = request.Method;
        var url = request.Url;
        var body = request.Body;
        var headers = request.Headers;
        var redirects = new List<RedirectHop>();
        var wait = TimeSpan.Zero;
        long bytesSent = 0;
        var staleRetries = 0;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                using var message = CreateMessage(method, url, headers, body, jar, out var error);
                if (error is not null)
                {
                    return error;
                }

                message.Options.Set(TimingsKey, timings);
                bytesSent += message.Content?.Headers.ContentLength ?? 0;
                var hopStart = stopwatch.Elapsed;
                var setupBefore = timings.Setup;
                var openedBefore = timings.Opened;
                HttpResponseMessage response;
                try
                {
                    response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                }
                catch (HttpRequestException ex) when (staleRetries < MaxStaleConnectionRetries
                    && timings.Opened == openedBefore
                    && IsStaleConnectionFailure(ex))
                {
                    // The request went out on a pooled connection the other end had already closed:
                    // nothing was answered, so send it again (as browsers do).
                    staleRetries++;
                    bytesSent -= message.Content?.Headers.ContentLength ?? 0;
                    continue;
                }

                wait += stopwatch.Elapsed - hopStart - (timings.Setup - setupBefore);

                var responseHeaders = response.Headers
                    .Concat(response.Content.Headers)
                    .SelectMany(header => header.Value.Select(value => KeyValuePair.Create(header.Key, value)))
                    .ToList();
                StoreCookies(jar, url, responseHeaders);

                if (redirects.Count < MaxRedirects && RedirectTarget(response, url) is { } location)
                {
                    response.Dispose();
                    redirects.Add(new RedirectHop(method, url, (int)response.StatusCode, response.ReasonPhrase, location, responseHeaders, stopwatch.Elapsed - hopStart)
                    {
                        SentHeaders = SentHeaders(message),
                    });
                    (method, body) = RedirectedMethod((int)response.StatusCode, method, body);
                    // Like HttpClient's own redirects: credentials are not sent on to the new location.
                    headers = headers.Where(header => !header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)).ToList();
                    url = location;
                    continue;
                }

                using (response)
                {
                    var downloadStart = stopwatch.Elapsed;
                    var (content, truncated) = await ReadBodyAsync(response.Content, timeout.Token);
                    stopwatch.Stop();

                    return new HttpResponseData((int)response.StatusCode, response.ReasonPhrase, responseHeaders, content, truncated, stopwatch.Elapsed)
                    {
                        Redirects = redirects,
                        Timings = new ResponseTimings(timings.Dns, timings.Connect, timings.Tls, wait, stopwatch.Elapsed - downloadStart),
                        Sent = new SentRequest(method, url, SentHeaders(message)) { Parts = SentParts(message.Content) },
                        Cookies = jar is null ? null : FromContainer(jar),
                        BytesSent = bytesSent,
                    };
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HttpErrors.Timeout(_timeout);
        }
        catch (HttpRequestException ex)
        {
            return HttpErrors.NetworkError(url.Authority, Describe(ex));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new Error(ErrorCodes.Io, $"Could not read a request body file: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    /// <summary>Resolves the host and opens a TCP connection, timing each step.</summary>
    private static async ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, ConnectionTimings? timings, CancellationToken cancellationToken)
    {
        timings?.Opening();
        var host = endPoint.Host.Trim('[', ']');
        var started = Stopwatch.GetTimestamp();
        var addresses = IPAddress.TryParse(host, out var address)
            ? [address]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        timings?.AddDns(Stopwatch.GetElapsedTime(started));
        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        started = Stopwatch.GetTimestamp();
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endPoint.Port, cancellationToken);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        timings?.Connected(Stopwatch.GetElapsedTime(started));
        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>
    /// The absolute URL a redirect response points to, or <c>null</c> when it isn't a redirect to
    /// follow: not 300–303/307/308, no usable <c>Location</c>, or a switch from https to http.
    /// </summary>
    private static Uri? RedirectTarget(HttpResponseMessage response, Uri url)
    {
        if ((int)response.StatusCode is not (300 or 301 or 302 or 303 or 307 or 308) || response.Headers.Location is not { } location)
        {
            return null;
        }

        if (!location.IsAbsoluteUri)
        {
            location = new Uri(url, location);
        }

        if (location.Scheme != Uri.UriSchemeHttp && location.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        if (url.Scheme == Uri.UriSchemeHttps && location.Scheme == Uri.UriSchemeHttp)
        {
            return null;
        }

        // A location without a fragment keeps the original one (RFC 9110, section 10.2.2).
        return location.Fragment.Length == 0 && url.Fragment.Length > 0 ? new UriBuilder(location) { Fragment = url.Fragment[1..] }.Uri : location;
    }

    /// <summary>The method and body after a redirect: 303, and 301/302 after a POST, switch to a GET without a body.</summary>
    private static (string Method, OutgoingBody? Body) RedirectedMethod(int status, string method, OutgoingBody? body)
    {
        var toGet = status switch
        {
            303 => !method.Equals("HEAD", StringComparison.OrdinalIgnoreCase),
            300 or 301 or 302 => method.Equals("POST", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
        return toGet ? ("GET", null) : (method, body);
    }

    private static HttpRequestMessage CreateMessage(
        string method,
        Uri url,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        OutgoingBody? body,
        CookieContainer? jar,
        out Error? error)
    {
        error = null;
        var message = new HttpRequestMessage(new HttpMethod(method), url);
        message.Content = body switch
        {
            TextBody text => new StringContent(text.Content, new UTF8Encoding(false), text.DefaultMediaType),
            BinaryBody binary => CreateFileContent(binary.FilePath, binary.DefaultMediaType),
            MultipartBody multipart => CreateMultipart(multipart),
            null => null,
        };

        var userAgentSet = false;
        foreach (var (name, value) in headers)
        {
            userAgentSet |= string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase);
            if (IsContentHeader(name))
            {
                if (message.Content is null)
                {
                    // Content headers need a body; without one there is nothing to describe.
                    continue;
                }

                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                    || (message.Content is MultipartFormDataContent && name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
                {
                    // HttpClient computes the length, and multipart bodies need the boundary it generates.
                    continue;
                }

                message.Content.Headers.Remove(name);
                if (!message.Content.Headers.TryAddWithoutValidation(name, value))
                {
                    error = new Error(ErrorCodes.InvalidRequest, $"The header '{name}' can't be sent.");
                }
            }
            else if (!message.Headers.TryAddWithoutValidation(name, value))
            {
                error = new Error(ErrorCodes.InvalidRequest, $"The header '{name}' can't be sent.");
            }
        }

        if (!userAgentSet)
        {
            message.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        }

        if (jar?.GetCookieHeader(url) is { Length: > 0 } cookies)
        {
            // Cookies the request sets itself come first; the jar's are added after them.
            var own = message.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : null;
            message.Headers.Remove("Cookie");
            message.Headers.TryAddWithoutValidation("Cookie", own is null ? cookies : $"{own}; {cookies}");
        }

        return message;
    }

    /// <summary>
    /// The headers of a request as sent, read after sending so the ones the handler adds to the
    /// message (<c>Accept-Encoding</c>) are there; a header with several values is one entry, as on
    /// the wire. Content headers (with the computed length) are
    /// included, and <c>Host</c>, which the handler writes from the URL without adding it to the
    /// message, comes first as on the wire.
    /// </summary>
    internal static IReadOnlyList<KeyValuePair<string, string>> SentHeaders(HttpRequestMessage message)
    {
        IEnumerable<KeyValuePair<string, HeaderStringValues>> headers = message.Headers.NonValidated;
        if (message.Content is { } content)
        {
            _ = content.Headers.ContentLength;
            headers = headers.Concat(content.Headers.NonValidated);
        }

        var sent = headers
            .Select(header => KeyValuePair.Create(header.Key, header.Value.ToString()))
            .ToList();
        if (message.Headers.Host is null && message.RequestUri is { IsAbsoluteUri: true } url)
        {
            sent.Insert(0, KeyValuePair.Create("Host", HostHeader(url)));
        }

        return sent;
    }

    /// <summary>The <c>Host</c> header for a URL: the ASCII (punycode) host, and the port unless it's the scheme's default.</summary>
    private static string HostHeader(Uri url)
    {
        var host = url.HostNameType == UriHostNameType.IPv6 ? $"[{url.IdnHost.Trim('[', ']')}]" : url.IdnHost;
        return url.IsDefaultPort ? host : $"{host}:{url.Port}";
    }

    private static CookieContainer ToContainer(IReadOnlyList<StoredCookie> cookies)
    {
        var container = new CookieContainer();
        foreach (var cookie in cookies)
        {
            try
            {
                var restored = new Cookie(cookie.Name, cookie.Value, cookie.Path)
                {
                    Secure = cookie.Secure,
                    HttpOnly = cookie.HttpOnly,
                    Expires = cookie.Expires?.UtcDateTime ?? DateTime.MinValue,
                };
                if (cookie.Domain.StartsWith('.'))
                {
                    // A domain cookie: sent to the domain and its subdomains.
                    restored.Domain = cookie.Domain;
                    container.Add(restored);
                }
                else
                {
                    // A host-only cookie: added for its host, without a Domain, so it stays host-only.
                    container.Add(new Uri($"https://{cookie.Domain}{cookie.Path}"), restored);
                }
            }
            catch (CookieException)
            {
                // A cookie the container can't hold (e.g. edited by hand) is dropped.
            }
        }

        return container;
    }

    private static List<StoredCookie> FromContainer(CookieContainer container) =>
        container.GetAllCookies()
            .Where(cookie => !cookie.Expired)
            .Select(cookie => new StoredCookie(
                cookie.Name,
                cookie.Value,
                cookie.Domain,
                string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
                cookie.Expires == DateTime.MinValue ? null : new DateTimeOffset(cookie.Expires.ToUniversalTime()),
                cookie.Secure,
                cookie.HttpOnly))
            .ToList();

    /// <summary>
    /// Writes a <c>Domain</c> attribute with a leading dot. The container then reports domain
    /// cookies as <c>.example.com</c> and host-only ones (no <c>Domain</c>) as <c>example.com</c>,
    /// which is how <see cref="StoredCookie.Domain"/> tells them apart; both match the same hosts
    /// (RFC 6265 ignores the dot).
    /// </summary>
    private static string WithDottedDomain(string header) =>
        string.Join(';', header.Split(';').Select(part =>
        {
            var equals = part.IndexOf('=');
            if (equals < 0 || !part[..equals].Trim().Equals("domain", StringComparison.OrdinalIgnoreCase))
            {
                return part;
            }

            var domain = part[(equals + 1)..].Trim();
            return domain.Length == 0 || domain.StartsWith('.') ? part : $"{part[..(equals + 1)]}.{domain}";
        }));

    private static void StoreCookies(CookieContainer? jar, Uri url, IEnumerable<KeyValuePair<string, string>> headers)
    {
        if (jar is null)
        {
            return;
        }

        foreach (var (name, value) in headers)
        {
            if (!name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                jar.SetCookies(url, WithDottedDomain(value));
            }
            catch (CookieException)
            {
                // Browsers ignore cookies they can't accept (e.g. for another domain); so does the jar.
            }
        }
    }

    private static MultipartFormDataContent CreateMultipart(MultipartBody body)
    {
        var content = new MultipartFormDataContent();
        try
        {
            foreach (var field in body.Fields)
            {
                if (field.FilePath is null)
                {
                    var text = new StringContent(field.Value ?? string.Empty, new UTF8Encoding(false));
                    if (field.ContentType is not null)
                    {
                        text.Headers.ContentType = MediaTypeHeaderValue.Parse(field.ContentType);
                    }

                    content.Add(text, field.Name);
                    continue;
                }

                var file = CreateFileContent(field.FilePath, field.ContentType ?? FileMediaTypes.ForPath(field.FilePath));
                content.Add(file, field.Name, field.FileName ?? Path.GetFileName(field.FilePath));
            }
        }
        catch
        {
            content.Dispose();
            throw;
        }

        return content;
    }

    /// <summary>A summary of each part of a multipart body (not its content), for the request shown as sent.</summary>
    private static List<SentPart>? SentParts(HttpContent? content) =>
        content is MultipartFormDataContent multipart
            ? multipart
                .Select(part => new SentPart(
                    part.Headers.ContentDisposition?.Name?.Trim('"') ?? string.Empty,
                    part.Headers.ContentDisposition?.FileNameStar ?? part.Headers.ContentDisposition?.FileName?.Trim('"'),
                    part.Headers.ContentType?.ToString(),
                    part.Headers.ContentLength ?? 0))
                .ToList()
            : null;

    private static StreamContent CreateFileContent(string path, string mediaType)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new IOException($"'{path}': {ex.Message}", ex);
        }

        var content = new StreamContent(stream);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        return content;
    }

    private static bool IsContentHeader(string name) =>
        name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Expires", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Last-Modified", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Allow", StringComparison.OrdinalIgnoreCase);

    private async Task<(byte[] Body, bool Truncated)> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            var room = _maxBodyBytes - (int)buffer.Length;
            if (read > room)
            {
                buffer.Write(chunk, 0, room);
                return (buffer.ToArray(), true);
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), false);
    }

    /// <summary>
    /// Whether a send that didn't open a connection of its own failed the way a request sent over a
    /// connection closed at the other end does: reset, or closed before any response arrived.
    /// <see cref="SocketsHttpHandler"/> doesn't retry these itself.
    /// </summary>
    private static bool IsStaleConnectionFailure(HttpRequestException ex) =>
        ex.HttpRequestError is HttpRequestError.ResponseEnded
        || (ex.HttpRequestError is HttpRequestError.Unknown && ex.InnerException is IOException);

    /// <summary>The most specific message of the failure, e.g. "Connection refused".</summary>
    private static string Describe(HttpRequestException ex)
    {
        Exception inner = ex;
        while (inner.InnerException is not null && inner is not SocketException and not AuthenticationException)
        {
            inner = inner.InnerException;
        }

        return inner.Message;
    }
}
