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
/// Sends requests with <see cref="HttpClient"/>. Redirects are followed, responses are
/// decompressed, and no cookies are kept between requests.
/// </summary>
public sealed class HttpClientSender : IHttpSender, IDisposable
{
    /// <summary>The User-Agent sent when the request doesn't set one.</summary>
    public const string DefaultUserAgent = "Missive";

    /// <summary>Bodies larger than this are cut off.</summary>
    public const int DefaultMaxBodyBytes = 20 * 1024 * 1024;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly TimeSpan _timeout;
    private readonly int _maxBodyBytes;

    public HttpClientSender()
        : this(new HttpClient(CreateHandler()), ownsClient: true)
    {
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

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(30),
    };

    public async Task<Result<HttpResponseData>> SendAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
    {
        using var message = CreateMessage(request, out var error);
        if (error is not null)
        {
            return error;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var (body, truncated) = await ReadBodyAsync(response.Content, timeout.Token);
            stopwatch.Stop();

            var headers = response.Headers
                .Concat(response.Content.Headers)
                .SelectMany(header => header.Value.Select(value => KeyValuePair.Create(header.Key, value)))
                .ToList();

            return new HttpResponseData((int)response.StatusCode, response.ReasonPhrase, headers, body, truncated, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HttpErrors.Timeout(_timeout);
        }
        catch (HttpRequestException ex)
        {
            return HttpErrors.NetworkError(request.Url.Authority, Describe(ex));
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private static HttpRequestMessage CreateMessage(OutgoingRequest request, out Error? error)
    {
        error = null;
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        message.Content = request.Body switch
        {
            TextBody text => new StringContent(text.Content, new UTF8Encoding(false), text.DefaultMediaType),
            MultipartBody multipart => CreateMultipart(multipart),
            _ => null,
        };

        var userAgentSet = false;
        foreach (var (name, value) in request.Headers)
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

        return message;
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
