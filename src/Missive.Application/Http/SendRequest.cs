using Missive.Application.Captures;
using Missive.Application.Common;
using Missive.Application.Cookies;
using Missive.Application.Environments;
using Missive.Application.Folders;
using Missive.Application.History;
using Missive.Application.OAuth2;
using Missive.Application.Requests;
using Missive.Application.Workspaces;
using Missive.Domain.Http;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;

namespace Missive.Application.Http;

/// <summary>
/// Sends the request currently in the editor (saved or not) and returns the response. The request
/// can be cancelled with <see cref="CancelRequest"/> using the same id. <c>{{variable}}</c>
/// references are resolved with the selected environment. When that environment has a cookie
/// jar, its cookies are sent and the cookies responses set are kept in it. The values the
/// request's capture rules pick out of a successful response are stored for that environment in
/// <see cref="ICapturedValueStore"/>. The raw body is kept in <see cref="ResponseBodies"/> under
/// the same id, for <see cref="SaveResponseBody"/>. A request with OAuth 2.0 auth gets its token
/// from <see cref="OAuth2Tokens"/> (cached, refreshed or newly obtained) before it is sent. The
/// response to a saved request is kept in its history by <see cref="ResponseHistory"/>. The request
/// gets the headers and auth of the folders it is in (see <see cref="FolderInheritance"/>).
/// </summary>
public sealed class SendRequest(
    IHttpSender sender,
    InFlightRequests inFlight,
    ActiveEnvironment environment,
    ResponseBodies bodies,
    ICookieJarStore cookieJars,
    ICapturedValueStore captures,
    CurrentWorkspace current,
    TimeProvider time,
    OAuth2Tokens tokens,
    ResponseHistory history,
    FolderInheritance inheritance)
{
    /// <param name="id">An id chosen by the caller, used to cancel the request.</param>
    /// <param name="path">
    /// The workspace path of the request file the request in the editor belongs to; its response is
    /// kept in that file's history. <c>null</c> for a request that hasn't been saved.
    /// </param>
    /// <param name="folder">
    /// The folder an unsaved request will be saved in, whose headers and auth it inherits; a saved
    /// request inherits those of the folder its file is in.
    /// </param>
    public async Task<Result<ResponseDto>> ExecuteAsync(
        string id,
        RequestDto request,
        string? path = null,
        string? folder = null,
        CancellationToken cancellationToken = default)
    {
        // The name doesn't matter for sending; an unnamed request can still be tried out.
        var named = string.IsNullOrWhiteSpace(request.Name) ? request with { Name = "Untitled" } : request;
        var definition = RequestMapper.ToDomain(named);
        if (definition.IsFailure)
        {
            return definition.Error!;
        }

        // The workspace can change while the request is in flight; cookies belong to the one it was sent from.
        var root = current.Root;
        if (root is not null && InheritsFrom(path, folder) is { } parent)
        {
            definition = await inheritance.ApplyAsync(root, parent, definition.Value, cancellationToken);
            if (definition.IsFailure)
            {
                return definition.Error!;
            }
        }

        var variables = await environment.LoadAsync(cancellationToken);
        if (!variables.IsSuccess)
        {
            return variables.Error!;
        }

        // An OAuth 2.0 token is only worth obtaining for a request that can be sent: check it first
        // with a stand-in token.
        var oauth = definition.Value.Auth as OAuth2Auth;
        var prepared = RequestPreparation.Prepare(definition.Value, variables.Value.Values, oauth is null ? null : "token", root);
        if (!prepared.IsValid)
        {
            return new Error(ErrorCodes.InvalidRequest, variables.Value.Explain(prepared.Errors, prepared.MissingVariables));
        }

        OAuth2Settings? oauthSettings = null;
        if (oauth is not null)
        {
            var resolution = OAuth2Flow.Resolve(oauth, variables.Value.Values);
            if (!resolution.IsValid)
            {
                return new Error(ErrorCodes.InvalidRequest, variables.Value.Explain(resolution.Errors, resolution.MissingVariables));
            }

            oauthSettings = resolution.Settings;
        }

        var outgoing = prepared.Request!;
        var jarId = variables.Value.CookieJar && root is not null && current.Root == root ? variables.Value.EnvironmentId : null;
        if (jarId is not null)
        {
            var jar = await CookieJars.LoadAsync(cookieJars, root!, jarId, time.GetUtcNow(), cancellationToken);
            if (!jar.IsSuccess)
            {
                return jar.Error!;
            }

            outgoing = outgoing with { Cookies = jar.Value };
        }

        var sentAt = time.GetUtcNow();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!inFlight.TryStart(id, cancellation))
        {
            return HttpErrors.AlreadySending(id);
        }

        try
        {
            if (oauthSettings is not null)
            {
                // Obtained inside the in-flight window so Cancel also stops a slow token endpoint.
                var token = await tokens.GetAsync(oauthSettings, root, variables.Value.EnvironmentId, cancellation.Token);
                if (!token.IsSuccess)
                {
                    return token.Error!;
                }

                var withToken = RequestPreparation.Prepare(definition.Value, variables.Value.Values, token.Value.AccessToken, root).Request!;
                outgoing = withToken with { Cookies = outgoing.Cookies };
            }

            var response = await sender.SendAsync(outgoing, cancellation.Token);
            if (!response.IsSuccess)
            {
                return response.Error!;
            }

            if (jarId is not null && response.Value.Cookies is { } cookies)
            {
                // The response is worth showing even if the jar can't be written.
                await CookieJars.ReplaceAsync(cookieJars, root!, jarId, CookieJars.Sorted(cookies, time.GetUtcNow()), CancellationToken.None);
            }

            var masking = Masking.For(definition.Value.Auth, variables.Value);
            var dto = ToDto(response.Value, outgoing.Url, masking, time.GetUtcNow());
            if (definition.Value.Captures.Count > 0)
            {
                dto = dto with
                {
                    Captures = await CaptureAsync(definition.Value.Captures, response.Value, dto, root, variables.Value.EnvironmentId),
                };
            }

            bodies.Add(id, new StoredBody(response.Value.Body, dto.FileName));
            if (root is not null && current.Root == root)
            {
                var historyId = await history.RecordAsync(
                    root,
                    path,
                    outgoing.Method,
                    dto.Sent?.Url ?? masking.Url(outgoing.Url),
                    dto,
                    variables.Value.SecretValues ?? [],
                    sentAt);
                dto = dto with { HistoryId = historyId };
            }

            return dto;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return HttpErrors.Cancelled();
        }
        finally
        {
            inFlight.Finish(id);
        }
    }

    /// <summary>The folder a request inherits headers and auth from: its file's, or the one it will be saved in.</summary>
    private static WorkspacePath? InheritsFrom(string? path, string? folder)
    {
        if (path is not null)
        {
            return WorkspacePath.TryParse(path, out var file) && WorkspaceLayout.IsRequestPath(file) ? file.Parent : null;
        }

        return WorkspacePath.TryParse(folder, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Picks the captured values out of the response and stores them for the environment the
    /// request was sent with. The response is worth showing even when they can't be stored, so
    /// failures are reported per capture instead.
    /// </summary>
    private async Task<IReadOnlyList<CaptureResultDto>> CaptureAsync(
        IReadOnlyList<CaptureRule> rules,
        HttpResponseData response,
        ResponseDto dto,
        string? root,
        string? environmentId)
    {
        if (!ResponseCaptures.Applies(response.StatusCode))
        {
            var skipped = $"Not captured: values are only captured from successful (2xx) responses, and this one is {response.StatusCode}.";
            return rules.Select(rule => new CaptureResultDto(rule.Variable, rule.Expression, null, skipped)).ToList();
        }

        var results = ResponseCaptures.Evaluate(
            rules,
            response.Headers,
            dto.BodyEncoding == ResponseBodyDecoder.Text ? dto.Body : null,
            response.Truncated);
        var captured = results.Where(result => result.Value is not null).ToList();
        if (captured.Count == 0)
        {
            return results;
        }

        string? failure = null;
        if (environmentId is null)
        {
            failure = "Not stored: no environment is selected. Captured values are kept per environment.";
        }
        else if (root is null || current.Root != root)
        {
            failure = "Not stored: the workspace was closed while the request was being sent.";
        }
        else
        {
            var stored = await CapturedValues.UpdateAsync(
                captures,
                root,
                environmentId,
                values =>
                {
                    var updated = new Dictionary<string, string>(values, StringComparer.Ordinal);
                    foreach (var result in captured)
                    {
                        updated[result.Variable] = result.Value!;
                    }

                    return updated;
                },
                CancellationToken.None);
            failure = stored.IsSuccess ? null : $"Not stored: {stored.Error!.Message}";
        }

        return failure is null
            ? results
            : results.Select(result => result.Value is null ? result : result with { Value = null, Error = failure }).ToList();
    }

    private static ResponseDto ToDto(HttpResponseData response, Uri requestUrl, Masking masking, DateTimeOffset now)
    {
        var contentType = Header(response.Headers, "Content-Type");
        var (mediaType, charset) = ResponseBodyDecoder.ParseContentType(contentType);
        var (body, encoding) = ResponseBodyDecoder.Decode(response.Body, mediaType, charset);
        var finalUrl = response.Sent?.Url ?? response.Redirects.LastOrDefault()?.Location ?? requestUrl;

        var setCookies = response.Redirects
            .SelectMany(hop => hop.Headers)
            .Concat(response.Headers)
            .Where(header => string.Equals(header.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .Select(header => SetCookieHeader.Parse(header.Value))
            .OfType<ResponseCookie>()
            .Select(cookie => new ResponseCookieDto(
                cookie.Name,
                cookie.Value,
                cookie.Domain,
                cookie.Path,
                cookie.MaxAge is { } maxAge ? now.AddSeconds(Math.Clamp(maxAge, -1_000_000_000L, 1_000_000_000L)) : cookie.Expires,
                cookie.Secure,
                cookie.HttpOnly,
                cookie.SameSite))
            .ToList();

        return new ResponseDto(
            response.StatusCode,
            response.ReasonPhrase ?? string.Empty,
            response.Headers.Select(header => new KeyValueDto(header.Key, header.Value)).ToList(),
            mediaType,
            body,
            encoding,
            response.Body.LongLength,
            response.Truncated,
            (long)Math.Round(response.Duration.TotalMilliseconds),
            ResponseFileName.Suggest(Header(response.Headers, "Content-Disposition"), finalUrl, mediaType),
            response.Timings is { } t
                ? new TimingsDto(Ms(t.Dns), Ms(t.Connect), Ms(t.Tls), Ms(t.Wait)!.Value, Ms(t.Download)!.Value)
                : null,
            response.Redirects
                .Select(hop => new RedirectDto(
                    hop.Method,
                    masking.Url(hop.Url),
                    hop.StatusCode,
                    hop.ReasonPhrase ?? string.Empty,
                    masking.Url(hop.Location),
                    (long)Math.Round(hop.Duration.TotalMilliseconds),
                    masking.Headers(hop.SentHeaders)))
                .ToList(),
            response.Sent is { } sent
                ? new SentRequestDto(
                    sent.Method,
                    masking.Url(sent.Url),
                    masking.Headers(sent.Headers),
                    sent.Parts?.Select(part => new SentPartDto(part.Name, part.FileName, part.ContentType, part.Size)).ToList())
                : null,
            setCookies);
    }

    private static string? Header(IEnumerable<KeyValuePair<string, string>> headers, string name) =>
        headers.FirstOrDefault(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static double? Ms(TimeSpan? span) => span is { } value ? Math.Round(value.TotalMilliseconds, 1) : null;

    /// <summary>What to hide in the request shown as sent: secret values and the credentials auth added.</summary>
    private sealed record Masking(IReadOnlyCollection<string> Secrets, IReadOnlyCollection<string> SensitiveHeaders, IReadOnlyCollection<string> SensitiveParams)
    {
        public static Masking For(RequestAuth auth, ActiveVariables variables)
        {
            var secrets = variables.SecretValues ?? [];
            if (auth is not ApiKeyAuth apiKey)
            {
                return new Masking(secrets, [], []);
            }

            var key = VariableTemplate.Resolve(apiKey.Key, variables.Values, new SortedSet<string>(StringComparer.Ordinal)).Trim();
            return apiKey.In == ApiKeyLocation.Query
                ? new Masking(secrets, [], [key])
                : new Masking(secrets, [key], []);
        }

        public string Url(Uri url) => SecretMasking.MaskUrl(url, SensitiveParams, Secrets);

        public string Header(string name, string value) => SecretMasking.MaskHeader(name, value, SensitiveHeaders, Secrets);

        public List<KeyValueDto> Headers(IEnumerable<KeyValuePair<string, string>> headers) =>
            headers.Select(h => new KeyValueDto(h.Key, Header(h.Key, h.Value))).ToList();
    }
}
