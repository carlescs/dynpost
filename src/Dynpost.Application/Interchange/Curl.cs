using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Folders;
using Dynpost.Application.Requests;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Http;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Interchange;

/// <summary>The shell a cURL command is quoted for.</summary>
public enum CurlShell
{
    /// <summary>bash, zsh and other POSIX shells: single quotes, <c>\</c> line breaks.</summary>
    Posix,

    /// <summary>Windows Command Prompt: double quotes, <c>^</c> line breaks.</summary>
    Cmd,

    /// <summary>PowerShell (<c>curl.exe</c>): single quotes, backtick line breaks.</summary>
    PowerShell,
}

/// <summary>A body as a cURL command sends it.</summary>
public closed record CurlBody;

/// <summary><c>--data-raw</c>: text sent as written.</summary>
public sealed record CurlTextBody(string Text) : CurlBody;

/// <summary><c>--data-binary @file</c>: a file sent as the whole body.</summary>
public sealed record CurlFileBody(string Path) : CurlBody;

/// <summary><c>-d</c> / <c>--data-urlencode</c> fields of a <c>urlencoded</c> body.</summary>
public sealed record CurlUrlEncodedBody(IReadOnlyList<FormField> Fields) : CurlBody;

/// <summary><c>-F</c> fields of a multipart body.</summary>
public sealed record CurlFormBody(IReadOnlyList<FormField> Fields) : CurlBody;

/// <summary>A request as a cURL command: what <see cref="ICurlConverter.Write"/> writes.</summary>
/// <param name="User">For <c>-u user:password</c>: Basic auth (or Digest with <paramref name="Digest"/>).</param>
/// <param name="Insecure">Whether to add <c>-k</c> (don't verify TLS certificates).</param>
/// <param name="FollowRedirects">Whether to add <c>-L</c>.</param>
/// <param name="MaxTime">For <c>--max-time</c>; <c>null</c> for none.</param>
/// <param name="Http2">Whether to add <c>--http2</c>.</param>
public sealed record CurlCommand(
    string Method,
    string Url,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    CurlBody? Body = null,
    (string Name, string Password)? User = null,
    bool Digest = false,
    bool Insecure = false,
    bool FollowRedirects = true,
    TimeSpan? MaxTime = null,
    bool Http2 = false);

/// <summary>A request read from a cURL command, with what couldn't be represented.</summary>
public sealed record ParsedCurl(HttpRequestDefinition Request, IReadOnlyList<string> Warnings);

/// <summary>Reads and writes cURL commands.</summary>
public interface ICurlConverter
{
    /// <summary>
    /// Reads a <c>curl …</c> command (POSIX, cmd or PowerShell quoting), or says why it can't
    /// (<see cref="ErrorCodes.InvalidRequest"/>). Options Dynpost can't represent are listed as warnings.
    /// </summary>
    Result<ParsedCurl> Parse(string command);

    /// <summary>Writes a command, quoted for <paramref name="shell"/>, one option per line.</summary>
    string Write(CurlCommand command, CurlShell shell);
}

/// <summary>A request parsed from cURL, as the editor opens it.</summary>
public sealed record ParsedCurlDto(RequestDto Request, IReadOnlyList<string> Warnings);

/// <summary>Reads a <c>curl …</c> command into a request (not saved: the editor shows it).</summary>
public sealed class ParseCurl(ICurlConverter converter)
{
    public Result<ParsedCurlDto> Execute(string command)
    {
        var parsed = converter.Parse(command ?? string.Empty);
        return parsed.Map(p => new ParsedCurlDto(RequestMapper.ToDto(p.Request), p.Warnings));
    }
}

/// <summary>
/// Writes the request in the editor (saved or not) as a cURL command, with the headers and auth of
/// its folders. Resolved, its <c>{{variables}}</c> take the selected environment's values (and the
/// folders' and globals'), with secret values and credentials masked unless
/// <c>includeSecrets</c>; otherwise <c>{{variables}}</c> are kept as written.
/// </summary>
public sealed class CopyAsCurl(
    ICurlConverter converter,
    ActiveEnvironment environment,
    FolderInheritance inheritance,
    CurrentWorkspace current,
    TimeProvider time)
{
    /// <summary>Stands in for an OAuth 2.0 access token, which isn't fetched to write a command.</summary>
    public const string AccessTokenPlaceholder = "<access token>";

    public async Task<Result<string>> ExecuteAsync(
        RequestDto request,
        string? path,
        string? folder,
        bool resolve,
        bool includeSecrets,
        CurlShell shell,
        CancellationToken cancellationToken = default)
    {
        var named = string.IsNullOrWhiteSpace(request.Name) ? request with { Name = "Untitled" } : request;
        var definition = RequestMapper.ToDomain(named);
        if (!definition.IsSuccess)
        {
            return definition.Error!;
        }

        var inherited = InheritedDefaults.None;
        var root = current.Root;
        if (root is not null && InheritsFrom(path, folder) is { } parent)
        {
            var read = await inheritance.ForFolderAsync(root, parent, cancellationToken);
            if (!read.IsSuccess)
            {
                return read.Error!;
            }

            inherited = read.Value;
        }

        var applied = inherited.Apply(definition.Value);
        if (!resolve)
        {
            return converter.Write(CurlCommands.AsWritten(applied), shell);
        }

        var variables = await environment.LoadAsync(cancellationToken);
        if (variables.IsSuccess)
        {
            variables = FolderInheritance.WithVariables(inherited, variables.Value);
        }

        if (!variables.IsSuccess)
        {
            return variables.Error!;
        }

        var values = DynamicVariables.With(variables.Value.Values, time.GetUtcNow(), Random.Shared);
        var token = applied.Auth is OAuth2Auth ? AccessTokenPlaceholder : null;
        var prepared = RequestPreparation.Prepare(applied, values, token, root);
        if (!prepared.IsValid)
        {
            return new Error(ErrorCodes.InvalidRequest, variables.Value.Explain(prepared.Errors, prepared.MissingVariables));
        }

        var command = CurlCommands.Resolved(prepared.Request!);
        if (!includeSecrets)
        {
            command = CurlCommands.Masked(command, applied.Auth, values, variables.Value.SecretValues ?? []);
        }

        return converter.Write(command, shell);
    }

    private static WorkspacePath? InheritsFrom(string? path, string? folder)
    {
        if (path is not null)
        {
            return WorkspacePath.TryParse(path, out var file) && WorkspaceLayout.IsRequestPath(file) ? file.Parent : null;
        }

        return WorkspacePath.TryParse(folder, out var parsed) ? parsed : null;
    }
}

/// <summary>Builds <see cref="CurlCommand"/>s from requests.</summary>
public static class CurlCommands
{
    /// <summary>A prepared request (variables resolved, auth applied) as a command.</summary>
    public static CurlCommand Resolved(OutgoingRequest request)
    {
        CurlBody? body = request.Body switch
        {
            null => null,
            TextBody text => new CurlTextBody(text.Content),
            BinaryBody binary => new CurlFileBody(binary.FilePath),
            MultipartBody multipart => new CurlFormBody(multipart.Fields
                .Select(field => field.FilePath is null
                    ? new FormField(field.Name, field.Value ?? string.Empty, ContentType: field.ContentType ?? string.Empty)
                    : FormField.File(field.Name, field.FilePath, field.ContentType ?? string.Empty, field.FileName ?? string.Empty))
                .ToList()),
        };

        var headers = request.Headers.ToList();
        AddDefaultContentType(headers, request.Body);
        return new CurlCommand(
            request.Method,
            request.Url.AbsoluteUri,
            headers,
            body,
            request.Digest is { } digest ? (digest.Username, digest.Password) : null,
            request.Digest is not null,
            !request.Settings.EffectiveVerifyTls,
            request.Settings.EffectiveMaxRedirects > 0,
            request.Settings.Timeout,
            request.Settings.EffectiveHttpVersion == HttpVersionPreference.Http2);
    }

    /// <summary>A request as a command with its <c>{{variables}}</c> kept as written.</summary>
    public static CurlCommand AsWritten(HttpRequestDefinition request)
    {
        var url = PathParameters.Apply(request.Url, request.PathParams, value => value).Trim();
        var query = request.Params.Where(p => p.Enabled).Select(p => KeyValuePair.Create(p.Key, p.Value)).ToList();
        var headers = request.Headers.Where(h => h.Enabled).Select(h => KeyValuePair.Create(h.Key, h.Value)).ToList();
        (string, string)? user = null;
        var digest = false;
        switch (request.Auth)
        {
            case BasicAuth basic:
                user = (basic.Username, basic.Password);
                break;
            case DigestAuth digestAuth:
                user = (digestAuth.Username, digestAuth.Password);
                digest = true;
                break;
            case BearerAuth bearer:
                AddUnlessSet(headers, "Authorization", $"Bearer {bearer.Token}");
                break;
            case ApiKeyAuth { In: ApiKeyLocation.Query } apiKey:
                query.Add(KeyValuePair.Create(apiKey.Key, apiKey.Value));
                break;
            case ApiKeyAuth apiKey:
                AddUnlessSet(headers, apiKey.Key, apiKey.Value);
                break;
            case OAuth2Auth:
                AddUnlessSet(headers, "Authorization", $"Bearer {CopyAsCurl.AccessTokenPlaceholder}");
                break;
            case NoAuth or InheritAuth:
                break;
        }

        if (query.Count > 0)
        {
            var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            url += separator + string.Join('&', query.Select(p => $"{p.Key}={p.Value}"));
        }

        var body = request.Body;
        CurlBody? curlBody = body.Type switch
        {
            BodyType.None => null,
            BodyType.Json or BodyType.Text => new CurlTextBody(body.Content),
            BodyType.Graphql => new CurlTextBody(GraphqlPayload(body)),
            BodyType.Binary => new CurlFileBody(body.Content.Trim()),
            BodyType.UrlEncoded => new CurlUrlEncodedBody(FormFields.Of(body).Where(f => f.Enabled).ToList()),
            BodyType.Form => new CurlFormBody(FormFields.Of(body).Where(f => f.Enabled).ToList()),
            _ => null,
        };

        var defaultType = body.Type switch
        {
            BodyType.Json or BodyType.Graphql => "application/json",
            BodyType.Text => "text/plain",
            _ => null,
        };
        if (defaultType is not null)
        {
            AddUnlessSet(headers, "Content-Type", defaultType);
        }

        var method = body.Type == BodyType.Graphql ? "POST" : request.Method.ToName();
        return new CurlCommand(
            method,
            url,
            headers,
            curlBody,
            user,
            digest,
            !request.Settings.EffectiveVerifyTls,
            request.Settings.EffectiveMaxRedirects > 0,
            request.Settings.Timeout,
            request.Settings.EffectiveHttpVersion == HttpVersionPreference.Http2);
    }

    /// <summary>The command with credentials and secret values hidden (<see cref="SecretMasking.Mask"/>).</summary>
    public static CurlCommand Masked(CurlCommand command, RequestAuth auth, IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> secrets)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        IReadOnlyCollection<string> sensitiveHeaders = auth is ApiKeyAuth { In: ApiKeyLocation.Header } header
            ? [VariableTemplate.Resolve(header.Key, values, missing).Trim()]
            : [];
        IReadOnlyCollection<string> sensitiveParams = auth is ApiKeyAuth { In: ApiKeyLocation.Query } query
            ? [VariableTemplate.Resolve(query.Key, values, missing).Trim()]
            : [];

        var url = Uri.TryCreate(command.Url, UriKind.Absolute, out var uri)
            ? SecretMasking.MaskUrl(uri, sensitiveParams, secrets)
            : SecretMasking.MaskSecrets(command.Url, secrets);
        CurlBody? body = command.Body switch
        {
            CurlTextBody text => new CurlTextBody(SecretMasking.MaskSecrets(text.Text, secrets)),
            CurlUrlEncodedBody fields => new CurlUrlEncodedBody(fields.Fields.Select(f => f with { Value = SecretMasking.MaskSecrets(f.Value, secrets) }).ToList()),
            CurlFormBody fields => new CurlFormBody(fields.Fields.Select(f => f.IsFile ? f : f with { Value = SecretMasking.MaskSecrets(f.Value, secrets) }).ToList()),
            var other => other,
        };

        return command with
        {
            Url = url,
            Headers = command.Headers
                .Select(h => KeyValuePair.Create(h.Key, SecretMasking.MaskHeader(h.Key, h.Value, sensitiveHeaders, secrets)))
                .ToList(),
            Body = body,
            User = command.User is { } user ? (user.Name, SecretMasking.Mask) : null,
        };
    }

    private static string GraphqlPayload(RequestBody body)
    {
        var query = System.Text.Json.JsonSerializer.Serialize(body.Content);
        var variables = string.IsNullOrWhiteSpace(body.Variables) ? "{}" : body.Variables.Trim();
        return $"{{\"query\":{query},\"variables\":{variables}}}";
    }

    private static void AddDefaultContentType(List<KeyValuePair<string, string>> headers, OutgoingBody? body)
    {
        var type = body switch
        {
            TextBody text => text.DefaultMediaType,
            BinaryBody binary => binary.DefaultMediaType,
            _ => null,
        };
        if (type is not null)
        {
            AddUnlessSet(headers, "Content-Type", type);
        }
    }

    private static void AddUnlessSet(List<KeyValuePair<string, string>> headers, string name, string value)
    {
        if (!headers.Any(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)))
        {
            headers.Add(KeyValuePair.Create(name, value));
        }
    }
}
