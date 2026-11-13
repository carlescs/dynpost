using System.Text.Json;
using System.Text.Json.Serialization;
using Dynpost.Application.Common;
using Dynpost.Application.OAuth2;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Environments;
using static Dynpost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynpost.Infrastructure.OAuth2;

/// <summary>
/// Keeps OAuth 2.0 tokens in <c>.dynpost/oauth2-tokens.json</c>, which is git-ignored: per
/// environment file name (the empty name for no environment), the tokens by the cache key of the
/// settings that obtained them.
/// </summary>
/// <example>
/// <code>
/// {
///   "dev.yaml": {
///     "3f9c2a7d1e0b4c58": {
///       "tokenUrl": "https://auth.example.com/oauth/token",
///       "clientId": "dynpost",
///       "scope": "read",
///       "accessToken": "eyJhbGciOi...",
///       "expires": "2026-11-07T13:00:00+00:00",
///       "obtained": "2026-11-07T12:00:00+00:00"
///     }
///   }
/// }
/// </code>
/// </example>
public sealed class JsonOAuth2TokenStore : IOAuth2TokenStore
{
    public const string FileName = "oauth2-tokens.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public async Task<Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, OAuth2Token>>>> LoadAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        string? text;
        try
        {
            text = await ReadIfExistsAsync(LocalWorkspaceFolder.FilePath(root, FileName), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read the OAuth 2.0 tokens file: {ex.Message}");
        }

        var tokens = new Dictionary<string, IReadOnlyDictionary<string, OAuth2Token>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return tokens;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, TokenEntry?>?>>(text, JsonOptions) ?? [];
            foreach (var (environment, entries) in stored)
            {
                tokens[environment] = (entries ?? [])
                    .Where(entry => !string.IsNullOrEmpty(entry.Value?.AccessToken))
                    .ToDictionary(
                        entry => entry.Key,
                        entry => new OAuth2Token(
                            entry.Value!.TokenUrl ?? string.Empty,
                            entry.Value.ClientId ?? string.Empty,
                            entry.Value.Scope ?? string.Empty,
                            entry.Value.AccessToken!,
                            string.IsNullOrEmpty(entry.Value.RefreshToken) ? null : entry.Value.RefreshToken,
                            entry.Value.Expires,
                            entry.Value.Obtained),
                        StringComparer.Ordinal);
            }
        }
        catch (JsonException ex)
        {
            return new Error(
                ErrorCodes.InvalidFile,
                $"'{WorkspaceLayout.LocalFolder}/{FileName}' is not a valid OAuth 2.0 tokens file. {ex.Message}");
        }

        return tokens;
    }

    public async Task<Result<Unit>> SaveAsync(
        string root,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, OAuth2Token>> tokens,
        CancellationToken cancellationToken = default)
    {
        var stored = new SortedDictionary<string, SortedDictionary<string, TokenEntry>>(StringComparer.Ordinal);
        foreach (var (environment, entries) in tokens.Where(entry => entry.Value.Count > 0))
        {
            stored[environment] = new SortedDictionary<string, TokenEntry>(
                entries.ToDictionary(
                    entry => entry.Key,
                    entry => new TokenEntry(
                        entry.Value.TokenUrl,
                        entry.Value.ClientId,
                        entry.Value.Scope,
                        entry.Value.AccessToken,
                        entry.Value.RefreshToken,
                        entry.Value.Expires,
                        entry.Value.Obtained)),
                StringComparer.Ordinal);
        }

        try
        {
            if (stored.Count == 0)
            {
                LocalWorkspaceFolder.Delete(root, FileName);
                return Unit.Value;
            }

            var json = JsonSerializer.Serialize(stored, JsonOptions) + "\n";
            await LocalWorkspaceFolder.WriteAsync(root, FileName, json, cancellationToken);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not save the OAuth 2.0 tokens file: {ex.Message}");
        }
    }

    private sealed record TokenEntry(
        string? TokenUrl,
        string? ClientId,
        string? Scope,
        string? AccessToken,
        string? RefreshToken,
        DateTimeOffset? Expires,
        DateTimeOffset Obtained);
}
