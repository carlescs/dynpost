using System.Diagnostics;
using System.Net;
using System.Net.Security;
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
/// when the request comes with a cookie jar. By default every send gets its own connection
/// handler, which measures DNS, TCP connect and TLS times; a client given to the constructor (in
/// tests) is shared and has no connection timings.
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

    private readonly HttpClient? _client;
    private readonly bool _ownsClient;
    private readonly TimeSpan _timeout;
    private readonly int _maxBodyBytes;

    /// <summary>Sends each request through a new handler of its own, with connection timings.</summary>
    public HttpClientSender()
    {
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
    /// A handler that doesn't follow redirects or keep cookies itself and reports connection
    /// phases to <paramref name="timings"/>.
    /// </summary>
    internal static SocketsHttpHandler CreateHandler(ConnectionTimings timings) => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(30),
        ConnectCallback = (context, cancellationToken) => ConnectAsync(context.DnsEndPoint, timings, cancellationToken),
        SslOptions = new SslClientAuthenticationOptions
        {
            // Called once the server's certificate has arrived, at the end of the handshake. It
            // accepts exactly what the default validation accepts.
            RemoteCertificateValidationCallback = (_, _, _, errors) =>
            {
                timings.HandshakeDone();
                return errors == SslPolicyErrors.None;
            },
        },
    };

    public async Task<Result<HttpResponseData>> SendAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
    {
        var timings = new ConnectionTimings();
        var client = _client ?? new HttpClient(CreateHandler(timings)) { Timeout = Timeout.InfiniteTimeSpan };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        var jar = request.Cookies is null ? null : ToContainer(request.Cookies);
        var method = request.Method;
        var url = request.Url;
        var body = request.Body;
        var headers = request.Headers;
        var redirects = new List<RedirectHop>();
        var wait = TimeSpan.Zero;
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

                var hopStart = stopwatch.Elapsed;
                var setupBefore = timings.Setup;
                var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                wait += stopwatch.Elapsed - hopStart - (timings.Setup - setupBefore);

                var responseHeaders = response.Headers
                    .Concat(response.Content.Headers)
                    .SelectMany(header => header.Value.Select(value => KeyValuePair.Create(header.Key, value)))
                    .ToList();
                StoreCookies(jar, url, responseHeaders);

                if (redirects.Count < MaxRedirects && RedirectTarget(response, url) is { } location)
                {
                    response.Dispose();
                    redirects.Add(new RedirectHop(method, url, (int)response.StatusCode, response.ReasonPhrase, location, responseHeaders, stopwatch.Elapsed - hopStart));
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
                        Sent = new SentRequest(method, url, SentHeaders(message)),
                        Cookies = jar is null ? null : FromContainer(jar),
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
        finally
        {
            if (_client is null)
            {
                client.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client?.Dispose();
        }
    }

    /// <summary>Resolves the host and opens a TCP connection, timing each step.</summary>
    private static async ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, ConnectionTimings timings, CancellationToken cancellationToken)
    {
        var host = endPoint.Host.Trim('[', ']');
        var started = Stopwatch.GetTimestamp();
        var addresses = IPAddress.TryParse(host, out var address)
            ? [address]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        timings.AddDns(Stopwatch.GetElapsedTime(started));
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

        timings.Connected(Stopwatch.GetElapsedTime(started));
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
            MultipartBody multipart => CreateMultipart(multipart),
            _ => null,
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

    /// <summary>The headers of a request as sent, content headers (with the computed length) included.</summary>
    private static IReadOnlyList<KeyValuePair<string, string>> SentHeaders(HttpRequestMessage message)
    {
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers = message.Headers;
        if (message.Content is { } content)
        {
            _ = content.Headers.ContentLength;
            headers = headers.Concat(content.Headers);
        }

        return headers
            .SelectMany(header => header.Value.Select(value => KeyValuePair.Create(header.Key, value)))
            .ToList();
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
        foreach (var (name, value) in body.Fields)
        {
            content.Add(new StringContent(value, new UTF8Encoding(false)), name);
        }

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
