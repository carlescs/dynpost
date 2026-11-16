using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Secrets;
using Dynpost.Application.Settings;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Environments;
using static Dynpost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynpost.Infrastructure.Secrets;

/// <summary>
/// Keeps secret values in the operating system's credential store: one item per secret, named
/// after the workspace folder, the environment and the variable. Which secrets a workspace has
/// (their names only, which are no secret) is listed in <c>.dynpost/keychain.json</c>, since
/// credential stores can't all be searched. Values are cached in memory after the first read.
/// </summary>
public sealed class KeychainSecretStore(IKeychain keychain) : ISecretStore
{
    public const string IndexFileName = "keychain.json";

    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> _cache = new(StringComparer.Ordinal);

    /// <summary>Whether the workspace lists secrets kept in the credential store.</summary>
    public static bool HasIndex(string root) => File.Exists(LocalWorkspaceFolder.FilePath(root, IndexFileName));

    public async Task<Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>> LoadAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(root, out var cached))
        {
            return Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>.Success(cached);
        }

        var index = await ReadIndexAsync(root, cancellationToken);
        if (!index.IsSuccess)
        {
            return index.Error!;
        }

        var secrets = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (id, names) in index.Value)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var value = await keychain.GetAsync(Account(root, id, name), cancellationToken);
                if (!value.IsSuccess)
                {
                    return value.Error!;
                }

                if (value.Value is { } found)
                {
                    values[name] = found;
                }
            }

            if (values.Count > 0)
            {
                secrets[id] = values;
            }
        }

        _cache[root] = secrets;
        return secrets;
    }

    public async Task<Result<Unit>> SaveAsync(
        string root,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> secrets,
        CancellationToken cancellationToken = default)
    {
        var previous = await LoadAsync(root, cancellationToken);
        if (!previous.IsSuccess)
        {
            return previous.Error!;
        }

        _cache.TryRemove(root, out _);
        foreach (var (id, values) in secrets)
        {
            foreach (var (name, value) in values)
            {
                if (previous.Value.GetValueOrDefault(id)?.GetValueOrDefault(name) == value)
                {
                    continue;
                }

                var set = await keychain.SetAsync(Account(root, id, name), value, cancellationToken);
                if (!set.IsSuccess)
                {
                    return set.Error!;
                }
            }
        }

        foreach (var (id, values) in previous.Value)
        {
            foreach (var name in values.Keys.Where(name => secrets.GetValueOrDefault(id)?.ContainsKey(name) != true))
            {
                var deleted = await keychain.DeleteAsync(Account(root, id, name), cancellationToken);
                if (!deleted.IsSuccess)
                {
                    return deleted.Error!;
                }
            }
        }

        var index = secrets
            .Where(entry => entry.Value.Count > 0)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Key, entry => entry.Value.Keys.Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        try
        {
            if (index.Count == 0)
            {
                LocalWorkspaceFolder.Delete(root, IndexFileName);
            }
            else
            {
                var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n";
                await LocalWorkspaceFolder.WriteAsync(root, IndexFileName, json, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not save the list of secrets: {ex.Message}");
        }

        _cache[root] = secrets
            .Where(entry => entry.Value.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        return Unit.Value;
    }

    /// <summary>The credential's account: the workspace folder (hashed), the environment and the variable.</summary>
    internal static string Account(string root, string environmentId, string name)
    {
        var folder = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..16];
        return $"{folder}/{environmentId}/{name}";
    }

    private static async Task<Result<Dictionary<string, List<string>>>> ReadIndexAsync(string root, CancellationToken cancellationToken)
    {
        try
        {
            var text = await ReadIfExistsAsync(LocalWorkspaceFolder.FilePath(root, IndexFileName), cancellationToken);
            return string.IsNullOrWhiteSpace(text)
                ? new Dictionary<string, List<string>>(StringComparer.Ordinal)
                : JsonSerializer.Deserialize<Dictionary<string, List<string>>>(text) ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            return new Error(ErrorCodes.InvalidFile, $"'{WorkspaceLayout.LocalFolder}/{IndexFileName}' is not a valid list of secrets. {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read the list of secrets: {ex.Message}");
        }
    }
}

/// <summary>
/// The secret store the user chose in the settings: <c>.dynpost/secrets.yaml</c> or the credential
/// store (when it is available). The first time a workspace's secrets are read after the choice
/// changed, they move from the other store to the chosen one.
/// </summary>
public sealed class SelectedSecretStore(
    YamlSecretStore file,
    KeychainSecretStore keychainStore,
    IKeychain keychain,
    IAppSettingsStore settings) : ISecretStore
{
    private readonly SemaphoreSlim _moving = new(1, 1);

    public async Task<Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>> LoadAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        var (chosen, other, hasOther) = await StoresAsync(root, cancellationToken);
        if (hasOther)
        {
            var moved = await MoveAsync(root, other, chosen, cancellationToken);
            if (!moved.IsSuccess)
            {
                return moved.Error!;
            }
        }

        return await chosen.LoadAsync(root, cancellationToken);
    }

    public async Task<Result<Unit>> SaveAsync(
        string root,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> secrets,
        CancellationToken cancellationToken = default)
    {
        var (chosen, _, _) = await StoresAsync(root, cancellationToken);
        return await chosen.SaveAsync(root, secrets, cancellationToken);
    }

    private async Task<(ISecretStore Chosen, ISecretStore Other, bool HasOther)> StoresAsync(string root, CancellationToken cancellationToken)
    {
        var useKeychain = (await settings.LoadAsync(cancellationToken)).Secrets is { Storage: SecretStorages.Keychain } && keychain.IsAvailable;
        return useKeychain
            ? (keychainStore, file, File.Exists(LocalWorkspaceFolder.FilePath(root, YamlSecretStore.FileName)))
            : (file, keychainStore, KeychainSecretStore.HasIndex(root) && keychain.IsAvailable);
    }

    /// <summary>Moves every secret of the workspace from one store to the other; the other keeps none.</summary>
    private async Task<Result<Unit>> MoveAsync(string root, ISecretStore from, ISecretStore to, CancellationToken cancellationToken)
    {
        await _moving.WaitAsync(cancellationToken);
        try
        {
            var values = await from.LoadAsync(root, cancellationToken);
            if (!values.IsSuccess)
            {
                return values.Error!;
            }

            var current = await to.LoadAsync(root, cancellationToken);
            if (!current.IsSuccess)
            {
                return current.Error!;
            }

            // Values already in the chosen store win over moved ones.
            var merged = new Dictionary<string, IReadOnlyDictionary<string, string>>(values.Value, StringComparer.Ordinal);
            foreach (var (id, kept) in current.Value)
            {
                var combined = new Dictionary<string, string>(merged.GetValueOrDefault(id) ?? new Dictionary<string, string>(), StringComparer.Ordinal);
                foreach (var (name, value) in kept)
                {
                    combined[name] = value;
                }

                merged[id] = combined;
            }

            var saved = await to.SaveAsync(root, merged, cancellationToken);
            return saved.IsSuccess
                ? await from.SaveAsync(root, new Dictionary<string, IReadOnlyDictionary<string, string>>(), cancellationToken)
                : saved.Error!;
        }
        finally
        {
            _moving.Release();
        }
    }
}
