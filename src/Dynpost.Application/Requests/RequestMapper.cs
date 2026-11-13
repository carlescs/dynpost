using Dynpost.Application.Common;
using Dynpost.Domain.Requests;

namespace Dynpost.Application.Requests;

/// <summary>Converts between the request DTOs used by the UI and the domain model.</summary>
public static class RequestMapper
{
    public static RequestDto ToDto(HttpRequestDefinition request) => new(
        request.Name,
        request.Method.ToName(),
        request.Url,
        request.Params.Select(ToDto).ToList(),
        request.Headers.Select(ToDto).ToList(),
        ToDto(request.Auth),
        new BodyDto(
            BodyTypeName(request.Body.Type),
            request.Body.Content,
            request.Body.Type == BodyType.Graphql ? request.Body.Variables : null,
            request.Body.Fields?.Select(ToDto).ToList()),
        request.Description,
        request.Captures.Select(c => new CaptureDto(c.Variable, c.Expression)).ToList());

    /// <summary>
    /// Builds a valid domain request from a DTO. Rows with neither key nor value (the blank row the
    /// editor keeps at the end of a table) are dropped.
    /// </summary>
    public static Result<HttpRequestDefinition> ToDomain(RequestDto dto)
    {
        if (!RequestMethods.TryParse(dto.Method, out var method))
        {
            return Invalid($"'{dto.Method}' is not a supported HTTP method.");
        }

        var auth = ToDomain(dto.Auth);
        if (!auth.IsSuccess)
        {
            return auth.Error!;
        }

        if (!TryParseBodyType(dto.Body?.Type, out var bodyType))
        {
            return Invalid($"'{dto.Body?.Type}' is not a supported body type.");
        }

        var request = new HttpRequestDefinition(
            (dto.Name ?? string.Empty).Trim(),
            method,
            dto.Url ?? string.Empty,
            ToEntries(dto.Params),
            ToEntries(dto.Headers),
            auth.Value,
            ToDomain(bodyType, dto.Body!))
        {
            Description = NormalizeDescription(dto.Description),
            Captures = (dto.Captures ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c.Variable) || !string.IsNullOrWhiteSpace(c.Expression))
                .Select(c => new CaptureRule((c.Variable ?? string.Empty).Trim(), (c.Expression ?? string.Empty).Trim()))
                .ToList(),
        };

        var errors = request.Validate();
        return errors.Count == 0 ? request : Invalid(string.Join(" ", errors));
    }

    private static RequestBody ToDomain(BodyType type, BodyDto dto)
    {
        if (type == BodyType.None)
        {
            return RequestBody.None;
        }

        // Only form and urlencoded bodies have fields; a body switched to another type keeps its text.
        if (dto.Fields is { } fields && type is BodyType.Form or BodyType.UrlEncoded)
        {
            return RequestBody.WithFields(type, fields
                .Where(f => !string.IsNullOrEmpty(f.Name) || !string.IsNullOrEmpty(f.Value)
                    || !string.IsNullOrEmpty(f.ContentType) || !string.IsNullOrEmpty(f.FileName))
                .Select(f => ToDomain(f, type)));
        }

        return new RequestBody(type, dto.Content ?? string.Empty, dto.Variables ?? string.Empty);
    }

    private static FormField ToDomain(FormFieldDto dto, BodyType type)
    {
        var file = type == BodyType.Form && string.Equals(dto.Kind, "file", StringComparison.OrdinalIgnoreCase);
        return new FormField(
            dto.Name ?? string.Empty,
            dto.Value ?? string.Empty,
            file ? FormFieldKind.File : FormFieldKind.Text,
            dto.Enabled,
            type == BodyType.Form ? (dto.ContentType ?? string.Empty).Trim() : string.Empty,
            file ? (dto.FileName ?? string.Empty).Trim() : string.Empty,
            type == BodyType.UrlEncoded && dto.Encode);
    }

    private static FormFieldDto ToDto(FormField field) => new(
        field.Name,
        field.Value,
        field.IsFile ? "file" : "text",
        field.Enabled,
        field.ContentType.Length > 0 ? field.ContentType : null,
        field.FileName.Length > 0 ? field.FileName : null,
        field.Encode);

    public static string BodyTypeName(BodyType type) => type switch
    {
        BodyType.None => "none",
        BodyType.Json => "json",
        BodyType.Text => "text",
        BodyType.Form => "form",
        BodyType.UrlEncoded => "urlencoded",
        BodyType.Graphql => "graphql",
        BodyType.Binary => "binary",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static bool TryParseBodyType(string? name, out BodyType type)
    {
        foreach (var candidate in Enum.GetValues<BodyType>())
        {
            if (string.Equals(BodyTypeName(candidate), name, StringComparison.OrdinalIgnoreCase))
            {
                type = candidate;
                return true;
            }
        }

        type = BodyType.None;
        return false;
    }

    /// <summary>
    /// Line endings become LF (files are LF-only), and text that is only whitespace counts as no
    /// description, so an emptied Docs tab leaves the key out of the file.
    /// </summary>
    public static string NormalizeDescription(string? description)
    {
        var text = (description ?? string.Empty).ReplaceLineEndings("\n");
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
    }

    private static KeyValueDto ToDto(KeyValueEntry entry) => new(entry.Key, entry.Value, entry.Enabled);

    /// <summary>The DTO of an auth setting.</summary>
    public static AuthDto ToDto(RequestAuth auth) => auth switch
    {
        BasicAuth basic => new AuthDto("basic", Username: basic.Username, Password: basic.Password),
        BearerAuth bearer => new AuthDto("bearer", Token: bearer.Token),
        ApiKeyAuth apiKey => new AuthDto("apiKey", Key: apiKey.Key, Value: apiKey.Value, In: apiKey.In == ApiKeyLocation.Query ? "query" : "header"),
        OAuth2Auth oauth => new AuthDto(
            "oauth2",
            Grant: GrantName(oauth.Grant),
            AuthUrl: oauth.AuthUrl,
            TokenUrl: oauth.TokenUrl,
            RedirectUri: oauth.RedirectUri,
            ClientId: oauth.ClientId,
            ClientSecret: oauth.ClientSecret,
            Scope: oauth.Scope,
            Audience: oauth.Audience,
            Credentials: oauth.Credentials == OAuth2ClientAuthentication.Body ? "body" : "header"),
        NoAuth => new AuthDto("none"),
        InheritAuth => new AuthDto("inherit"),
    };

    /// <summary>The name of a grant in request files and DTOs.</summary>
    public static string GrantName(OAuth2Grant grant) => grant switch
    {
        OAuth2Grant.ClientCredentials => "clientCredentials",
        OAuth2Grant.AuthorizationCode => "authorizationCode",
        _ => throw new ArgumentOutOfRangeException(nameof(grant)),
    };

    public static bool TryParseGrant(string? name, out OAuth2Grant grant)
    {
        foreach (var candidate in Enum.GetValues<OAuth2Grant>())
        {
            if (string.Equals(GrantName(candidate), name, StringComparison.OrdinalIgnoreCase))
            {
                grant = candidate;
                return true;
            }
        }

        grant = OAuth2Grant.ClientCredentials;
        return false;
    }

    /// <summary>Builds the domain auth from its DTO.</summary>
    public static Result<RequestAuth> ToDomain(AuthDto? auth) => auth?.Type?.ToLowerInvariant() switch
    {
        null or "" or "inherit" => RequestAuth.Inherit,
        "none" => RequestAuth.None,
        "basic" => new BasicAuth(auth.Username ?? string.Empty, auth.Password ?? string.Empty),
        "bearer" => new BearerAuth(auth.Token ?? string.Empty),
        "apikey" => auth.In?.ToLowerInvariant() switch
        {
            null or "" or "header" => new ApiKeyAuth(auth.Key ?? string.Empty, auth.Value ?? string.Empty, ApiKeyLocation.Header),
            "query" => new ApiKeyAuth(auth.Key ?? string.Empty, auth.Value ?? string.Empty, ApiKeyLocation.Query),
            _ => Result<RequestAuth>.Failure(ErrorCodes.InvalidRequest, $"'{auth.In}' is not a supported API key location."),
        },
        "oauth2" => OAuth2ToDomain(auth),
        _ => Result<RequestAuth>.Failure(ErrorCodes.InvalidRequest, $"'{auth.Type}' is not a supported auth type."),
    };

    private static Result<RequestAuth> OAuth2ToDomain(AuthDto auth)
    {
        var grant = OAuth2Grant.ClientCredentials;
        if (!string.IsNullOrEmpty(auth.Grant) && !TryParseGrant(auth.Grant, out grant))
        {
            return Invalid($"'{auth.Grant}' is not a supported OAuth 2.0 grant.");
        }

        var credentials = auth.Credentials?.ToLowerInvariant() switch
        {
            null or "" or "header" => OAuth2ClientAuthentication.Header,
            "body" => OAuth2ClientAuthentication.Body,
            _ => (OAuth2ClientAuthentication?)null,
        };
        if (credentials is null)
        {
            return Invalid($"'{auth.Credentials}' is not a supported way to send the client credentials.");
        }

        static string Field(string? value) => (value ?? string.Empty).Trim();
        return new OAuth2Auth(
            grant,
            Field(auth.TokenUrl),
            Field(auth.ClientId),
            auth.ClientSecret ?? string.Empty,
            grant == OAuth2Grant.AuthorizationCode ? Field(auth.AuthUrl) : string.Empty,
            grant == OAuth2Grant.AuthorizationCode ? Field(auth.RedirectUri) : string.Empty,
            Field(auth.Scope),
            Field(auth.Audience),
            credentials.Value);
    }

    internal static List<KeyValueEntry> ToEntries(IReadOnlyList<KeyValueDto>? rows) =>
        (rows ?? [])
            .Where(row => !string.IsNullOrEmpty(row.Key) || !string.IsNullOrEmpty(row.Value))
            .Select(row => new KeyValueEntry((row.Key ?? string.Empty).Trim(), row.Value ?? string.Empty, row.Enabled))
            .ToList();

    private static Error Invalid(string message) => new(ErrorCodes.InvalidRequest, message);
}
