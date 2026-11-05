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
    OutgoingBody? Body);

/// <summary>The body of an <see cref="OutgoingRequest"/>.</summary>
public abstract record OutgoingBody;

/// <summary>A body sent as text, with the media type to use when no Content-Type header is set.</summary>
public sealed record TextBody(string Content, string DefaultMediaType) : OutgoingBody;

/// <summary>A <c>multipart/form-data</c> body made of text fields.</summary>
public sealed record MultipartBody(IReadOnlyList<KeyValuePair<string, string>> Fields) : OutgoingBody;
