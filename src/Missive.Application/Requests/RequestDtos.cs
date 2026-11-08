namespace Missive.Application.Requests;

public sealed record KeyValueDto(string Key, string Value, bool Enabled = true);

/// <summary>Auth settings. Only the fields used by <see cref="Type"/> are meaningful.</summary>
/// <param name="Type"><c>none</c>, <c>basic</c>, <c>bearer</c>, <c>apiKey</c> or <c>oauth2</c>.</param>
/// <param name="In">For API keys: <c>header</c> or <c>query</c>.</param>
/// <param name="Grant">For OAuth 2.0: <c>clientCredentials</c> or <c>authorizationCode</c>.</param>
/// <param name="Credentials">For OAuth 2.0: how the client secret is sent, <c>header</c> or <c>body</c>.</param>
public sealed record AuthDto(
    string Type,
    string? Username = null,
    string? Password = null,
    string? Token = null,
    string? Key = null,
    string? Value = null,
    string? In = null,
    string? Grant = null,
    string? AuthUrl = null,
    string? TokenUrl = null,
    string? RedirectUri = null,
    string? ClientId = null,
    string? ClientSecret = null,
    string? Scope = null,
    string? Audience = null,
    string? Credentials = null);

/// <param name="Type"><c>none</c>, <c>json</c>, <c>text</c>, <c>form</c>, <c>urlencoded</c>, <c>graphql</c> or <c>binary</c>.</param>
public sealed record BodyDto(string Type, string Content, string? Variables = null);

/// <param name="Variable">The variable the value is stored in.</param>
/// <param name="Expression">Where the value comes from: a JSONPath (<c>$.token</c>) or <c>header:Name</c>.</param>
public sealed record CaptureDto(string Variable, string Expression);

/// <param name="Captures">Values to store in variables from a successful response.</param>
/// <param name="Description">Notes about the request, in Markdown; empty when there are none.</param>
public sealed record RequestDto(
    string Name,
    string Method,
    string Url,
    IReadOnlyList<KeyValueDto> Params,
    IReadOnlyList<KeyValueDto> Headers,
    AuthDto Auth,
    BodyDto Body,
    string? Description = null,
    IReadOnlyList<CaptureDto>? Captures = null);

/// <summary>A request as stored: its (possibly new) path and its content.</summary>
public sealed record SavedRequestDto(string Path, RequestDto Request);
