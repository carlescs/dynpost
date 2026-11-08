namespace Missive.Domain.Http;

/// <summary>
/// A request ready to go on the wire: variables resolved, query parameters in the URL, auth
/// turned into headers or parameters, and default headers added.
/// </summary>
/// <param name="Method">The method name, e.g. <c>GET</c>.</param>
/// <param name="Url">An absolute <c>http</c> or <c>https</c> URL.</param>
/// <param name="Headers">Headers in the order they are sent; names may repeat.</param>
public sealed record OutgoingRequest(
    string Method,
    Uri Url,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    OutgoingBody? Body)
{
    /// <summary>
    /// The cookie jar to send cookies from and to keep the cookies responses set in; <c>null</c>
    /// when no cookies are kept (only a <c>Cookie</c> header the request sets is sent).
    /// </summary>
    public IReadOnlyList<StoredCookie>? Cookies { get; init; }
}

/// <summary>The body of an <see cref="OutgoingRequest"/>.</summary>
public abstract record OutgoingBody;

/// <summary>A body sent as text, with the media type to use when no Content-Type header is set.</summary>
public sealed record TextBody(string Content, string DefaultMediaType) : OutgoingBody;

/// <summary>A body sent from a file, with the media type to use when no Content-Type header is set.</summary>
public sealed record BinaryBody(string FilePath, string DefaultMediaType = "application/octet-stream") : OutgoingBody;

/// <summary>A field in a <c>multipart/form-data</c> body.</summary>
public sealed record MultipartField(string Name, string? Value = null, string? FilePath = null, string? ContentType = null);

/// <summary>A <c>multipart/form-data</c> body made of text and file fields.</summary>
public sealed record MultipartBody(IReadOnlyList<MultipartField> Fields) : OutgoingBody;
