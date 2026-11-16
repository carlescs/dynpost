using Dynpost.Domain.Requests;

namespace Dynpost.Domain.Http;

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

    /// <summary>
    /// The credentials to answer a <c>Digest</c> challenge (a 401 response) with; <c>null</c> when
    /// the request doesn't use Digest auth.
    /// </summary>
    public DigestCredentials? Digest { get; init; }

    /// <summary>The client certificate to present when the server asks for one; <c>null</c> for none.</summary>
    public ClientCertificate? ClientCertificate { get; init; }

    /// <summary>How to send it: timeout, redirects, TLS checks and HTTP version (unset values take the defaults).</summary>
    public RequestSettings Settings { get; init; } = RequestSettings.Inherit;
}

/// <summary>The body of an <see cref="OutgoingRequest"/>.</summary>
public closed record OutgoingBody;

/// <summary>A body sent as text, with the media type to use when no Content-Type header is set.</summary>
public sealed record TextBody(string Content, string DefaultMediaType) : OutgoingBody;

/// <summary>A body sent from a file, with the media type to use when no Content-Type header is set.</summary>
public sealed record BinaryBody(string FilePath, string DefaultMediaType = "application/octet-stream") : OutgoingBody;

/// <summary>A field in a <c>multipart/form-data</c> body: text (<see cref="Value"/>) or a file (<see cref="FilePath"/>).</summary>
/// <param name="ContentType">The part's content type; <c>null</c> for none on text parts and one from the extension on files.</param>
/// <param name="FileName">The file name sent for a file part; <c>null</c> for the file's own name.</param>
public sealed record MultipartField(
    string Name,
    string? Value = null,
    string? FilePath = null,
    string? ContentType = null,
    string? FileName = null);

/// <summary>A <c>multipart/form-data</c> body made of text and file fields.</summary>
public sealed record MultipartBody(IReadOnlyList<MultipartField> Fields) : OutgoingBody;
