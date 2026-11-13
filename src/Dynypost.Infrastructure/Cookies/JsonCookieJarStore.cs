using System.Text.Json;
using System.Text.Json.Serialization;
using Dynypost.Application.Common;
using Dynypost.Application.Cookies;
using Dynypost.Domain.Http;
using Dynypost.Domain.Workspaces;
using Dynypost.Infrastructure.Environments;
using static Dynypost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynypost.Infrastructure.Cookies;

/// <summary>
/// Keeps the environments' cookie jars in <c>.dynypost/cookies.json</c>, which is git-ignored: one
/// list of cookies per environment file name.
/// </summary>
/// <example>
/// <code>
/// {
///   "dev.yaml": [
///     { "name": "session", "value": "abc", "domain": "localhost", "path": "/", "httpOnly": true }
///   ]
/// }
/// </code>
/// </example>
public sealed class JsonCookieJarStore : ICookieJarStore
{
    public const string FileName = "cookies.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public async Task<Result<IReadOnlyDictionary<string, IReadOnlyList<StoredCookie>>>> LoadAsync(
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
            return new Error(ErrorCodes.Io, $"Could not read the cookies file: {ex.Message}");
        }

        var jars = new Dictionary<string, IReadOnlyList<StoredCookie>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return jars;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, List<CookieEntry?>?>>(text, JsonOptions) ?? [];
            foreach (var (id, cookies) in stored)
            {
                jars[id] = (cookies ?? [])
                    .Where(cookie => !string.IsNullOrEmpty(cookie?.Name) && !string.IsNullOrEmpty(cookie.Domain))
                    .Select(cookie => new StoredCookie(
                        cookie!.Name!,
                        cookie.Value ?? string.Empty,
                        cookie.Domain!,
                        string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
                        cookie.Expires,
                        cookie.Secure,
                        cookie.HttpOnly))
                    .ToList();
            }
        }
        catch (JsonException ex)
        {
            return new Error(
                ErrorCodes.InvalidFile,
                $"'{WorkspaceLayout.LocalFolder}/{FileName}' is not a valid cookies file. {ex.Message}");
        }

        return jars;
    }

    public async Task<Result<Unit>> SaveAsync(
        string root,
        IReadOnlyDictionary<string, IReadOnlyList<StoredCookie>> jars,
        CancellationToken cancellationToken = default)
    {
        var stored = new SortedDictionary<string, List<CookieEntry>>(StringComparer.Ordinal);
        foreach (var (id, cookies) in jars.Where(entry => entry.Value.Count > 0))
        {
            stored[id] = cookies
                .Select(cookie => new CookieEntry(cookie.Name, cookie.Value, cookie.Domain, cookie.Path, cookie.Expires, cookie.Secure, cookie.HttpOnly))
                .ToList();
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
            return new Error(ErrorCodes.Io, $"Could not save the cookies file: {ex.Message}");
        }
    }

    private sealed record CookieEntry(
        string? Name,
        string? Value,
        string? Domain,
        string? Path,
        DateTimeOffset? Expires,
        bool Secure,
        bool HttpOnly);
}
