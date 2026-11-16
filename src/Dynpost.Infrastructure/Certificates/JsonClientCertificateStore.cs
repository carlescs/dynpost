using System.Text.Json;
using System.Text.Json.Serialization;
using Dynpost.Application.Certificates;
using Dynpost.Application.Common;
using Dynpost.Domain.Http;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Environments;
using static Dynpost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynpost.Infrastructure.Certificates;

/// <summary>
/// Keeps the environments' client certificates in <c>.dynpost/certificates.json</c>, which is
/// git-ignored: one list per environment file name, in the order they are matched.
/// </summary>
/// <example>
/// <code>
/// {
///   "dev.yaml": [
///     { "host": "api.example.com", "certificate": "certs/client.pfx", "passphrase": "s3cr3t" }
///   ]
/// }
/// </code>
/// </example>
public sealed class JsonClientCertificateStore : IClientCertificateStore
{
    public const string FileName = "certificates.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<Result<IReadOnlyDictionary<string, IReadOnlyList<ClientCertificateSetting>>>> LoadAsync(
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
            return new Error(ErrorCodes.Io, $"Could not read the client certificates file: {ex.Message}");
        }

        var result = new Dictionary<string, IReadOnlyList<ClientCertificateSetting>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, List<Entry?>?>>(text, JsonOptions) ?? [];
            foreach (var (id, entries) in stored)
            {
                result[id] = (entries ?? [])
                    .Where(entry => !string.IsNullOrEmpty(entry?.Certificate))
                    .Select(entry => new ClientCertificateSetting(
                        entry!.Host ?? string.Empty,
                        entry.Certificate!,
                        entry.Key ?? string.Empty,
                        entry.Passphrase ?? string.Empty))
                    .ToList();
            }
        }
        catch (JsonException ex)
        {
            return new Error(
                ErrorCodes.InvalidFile,
                $"'{WorkspaceLayout.LocalFolder}/{FileName}' is not a valid client certificates file. {ex.Message}");
        }

        return result;
    }

    public async Task<Result<Unit>> SaveAsync(
        string root,
        IReadOnlyDictionary<string, IReadOnlyList<ClientCertificateSetting>> certificates,
        CancellationToken cancellationToken = default)
    {
        var stored = new SortedDictionary<string, List<Entry>>(StringComparer.Ordinal);
        foreach (var (id, settings) in certificates.Where(entry => entry.Value.Count > 0))
        {
            stored[id] = settings
                .Select(s => new Entry(
                    s.Host.Length > 0 ? s.Host : null,
                    s.Certificate,
                    s.Key.Length > 0 ? s.Key : null,
                    s.Passphrase.Length > 0 ? s.Passphrase : null))
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
            return new Error(ErrorCodes.Io, $"Could not save the client certificates file: {ex.Message}");
        }
    }

    private sealed record Entry(string? Host, string? Certificate, string? Key, string? Passphrase);
}
