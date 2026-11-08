using Missive.Application.Common;
using Missive.Domain.Http;

namespace Missive.Application.Http;

/// <summary>Sends HTTP requests over the network.</summary>
public interface IHttpSender
{
    /// <summary>
    /// Sends the request and reads the whole response. Network failures and timeouts are returned
    /// as <see cref="HttpErrors"/>; cancellation through <paramref name="cancellationToken"/> throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<Result<HttpResponseData>> SendAsync(OutgoingRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A response as received. Redirects are followed; <see cref="Redirects"/> lists the hops before it.</summary>
/// <param name="Headers">Response and content headers, in the order received; names may repeat.</param>
/// <param name="Body">The (decompressed) body, cut off at the sender's size limit.</param>
/// <param name="Truncated">Whether the body was longer than the size limit.</param>
/// <param name="Duration">From sending the request until the whole body was read.</param>
public sealed record HttpResponseData(
    int StatusCode,
    string? ReasonPhrase,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    byte[] Body,
    bool Truncated,
    TimeSpan Duration)
{
    /// <summary>The request-body bytes sent across all redirect hops.</summary>
    public long BytesSent { get; init; }

    /// <summary>The redirect responses that led to this one, in order.</summary>
    public IReadOnlyList<RedirectHop> Redirects { get; init; } = [];

    /// <summary>Where the time went; <c>null</c> when it wasn't measured.</summary>
    public ResponseTimings? Timings { get; init; }

    /// <summary>The request that got this response, as sent (after redirects); <c>null</c> when unknown.</summary>
    public SentRequest? Sent { get; init; }

    /// <summary>
    /// The cookie jar after the exchange, when the request was sent with one
    /// (<see cref="OutgoingRequest.Cookies"/>); <c>null</c> otherwise.
    /// </summary>
    public IReadOnlyList<StoredCookie>? Cookies { get; init; }
}

/// <summary>A redirect response that was followed.</summary>
/// <param name="Url">The URL that answered with the redirect.</param>
/// <param name="Location">The absolute URL it redirected to.</param>
/// <param name="Headers">The redirect's headers (e.g. the cookies it set).</param>
public sealed record RedirectHop(
    string Method,
    Uri Url,
    int StatusCode,
    string? ReasonPhrase,
    Uri Location,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    TimeSpan Duration)
{
    /// <summary>
    /// The headers of the request that got the redirect, as sent. They can differ from the final
    /// request's: <c>Authorization</c> is not sent on to the new location.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> SentHeaders { get; init; } = [];
}

/// <summary>
/// The phases of an exchange, summed over every redirect hop. Connection phases are <c>null</c>
/// when no new connection was opened (or they couldn't be measured).
/// </summary>
/// <param name="Dns">Resolving host names.</param>
/// <param name="Connect">Opening TCP connections.</param>
/// <param name="Tls">TLS handshakes.</param>
/// <param name="Wait">From sending each request until its response headers arrived (time to first byte), without the phases above.</param>
/// <param name="Download">Reading the final response's body.</param>
public sealed record ResponseTimings(
    TimeSpan? Dns,
    TimeSpan? Connect,
    TimeSpan? Tls,
    TimeSpan Wait,
    TimeSpan Download);

/// <summary>A request as it went on the wire.</summary>
/// <param name="Headers">The headers sent, including the ones added by the sender (User-Agent, Content-Type, Cookie…).</param>
public sealed record SentRequest(string Method, Uri Url, IReadOnlyList<KeyValuePair<string, string>> Headers);
