namespace Missive.Application.Http;

/// <summary>A response as shown in the UI.</summary>
/// <param name="StatusText">The reason phrase, e.g. <c>Not Found</c>; may be empty.</param>
/// <param name="ContentType">The media type without parameters, e.g. <c>application/json</c>.</param>
/// <param name="Body">The body as text, or base64 when <paramref name="BodyEncoding"/> is <c>base64</c>.</param>
/// <param name="BodyEncoding"><c>text</c> or <c>base64</c> (for binary content).</param>
/// <param name="Size">Size of the received body in bytes.</param>
/// <param name="Truncated">Whether the body was too large and was cut off.</param>
public sealed record ResponseDto(
    int Status,
    string StatusText,
    IReadOnlyList<Requests.KeyValueDto> Headers,
    string? ContentType,
    string Body,
    string BodyEncoding,
    long Size,
    bool Truncated,
    long DurationMs);
