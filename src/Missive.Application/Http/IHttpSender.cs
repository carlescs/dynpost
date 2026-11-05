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

/// <summary>A response as received.</summary>
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
    TimeSpan Duration);
