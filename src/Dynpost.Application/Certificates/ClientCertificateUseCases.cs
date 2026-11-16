using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Http;

namespace Dynpost.Application.Certificates;

/// <summary>
/// The client certificates set up per environment, by environment id. They stay on this computer,
/// in the workspace's git-ignored <c>.dynpost</c> folder, since they name local files and hold
/// passphrases.
/// </summary>
public interface IClientCertificateStore
{
    /// <summary>Loads every environment's certificates; a missing store loads as empty.</summary>
    Task<Result<IReadOnlyDictionary<string, IReadOnlyList<ClientCertificateSetting>>>> LoadAsync(
        string root,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces every environment's certificates. Environments without any are left out.</summary>
    Task<Result<Unit>> SaveAsync(
        string root,
        IReadOnlyDictionary<string, IReadOnlyList<ClientCertificateSetting>> certificates,
        CancellationToken cancellationToken = default);
}

/// <summary>A client certificate of an environment, as the UI edits it. The passphrase is never sent back.</summary>
/// <param name="Host">The host it is for (<c>*.example.com</c> and <c>host:port</c> work); empty for every host.</param>
/// <param name="Certificate">The certificate file (PKCS#12 or PEM), workspace-relative or absolute.</param>
/// <param name="Key">The PEM private key file, when the key is in its own file.</param>
/// <param name="HasPassphrase">Whether a passphrase is stored.</param>
/// <param name="Passphrase">When saving: the new passphrase, <c>""</c> for none, <c>null</c> to keep the stored one.</param>
public sealed record ClientCertificateDto(
    string Host,
    string Certificate,
    string? Key = null,
    bool HasPassphrase = false,
    string? Passphrase = null);

/// <summary>Reading and updating the certificates of the environments, shared by the use cases.</summary>
public static class ClientCertificates
{
    /// <summary>
    /// The certificate to present for a request to <paramref name="url"/> sent with the environment
    /// <paramref name="environmentId"/>, with its paths resolved from <paramref name="root"/>;
    /// <c>null</c> when none is set up for the host, or why it can't be used.
    /// </summary>
    public static async Task<Result<ClientCertificate?>> ForAsync(
        IClientCertificateStore store,
        string root,
        string? environmentId,
        Uri url,
        CancellationToken cancellationToken)
    {
        if (environmentId is null || url.Scheme != Uri.UriSchemeHttps)
        {
            return (ClientCertificate?)null;
        }

        var stored = await store.LoadAsync(root, cancellationToken);
        if (!stored.IsSuccess)
        {
            return stored.Error!;
        }

        if (ClientCertificateSetting.For(stored.Value.GetValueOrDefault(environmentId) ?? [], url) is not { } setting)
        {
            return (ClientCertificate?)null;
        }

        var certificate = RequestFilePaths.Resolve(setting.Certificate.Trim(), root);
        if (certificate.Error is { } certificateError)
        {
            return new Error(ErrorCodes.InvalidRequest, $"The client certificate for {url.Host} can't be used: {certificateError}");
        }

        string? keyPath = null;
        if (setting.Key.Trim().Length > 0)
        {
            var key = RequestFilePaths.Resolve(setting.Key.Trim(), root);
            if (key.Error is { } keyError)
            {
                return new Error(ErrorCodes.InvalidRequest, $"The private key for {url.Host} can't be used: {keyError}");
            }

            keyPath = key.FullPath;
        }

        return new ClientCertificate(
            certificate.FullPath!,
            keyPath,
            setting.Passphrase.Length > 0 ? setting.Passphrase : null);
    }

    public static async Task MoveAsync(IClientCertificateStore store, string root, string from, string to, CancellationToken cancellationToken)
    {
        var stored = await store.LoadAsync(root, cancellationToken);
        if (stored.IsSuccess && stored.Value.TryGetValue(from, out var certificates))
        {
            var updated = stored.Value.Where(entry => entry.Key != from).ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            updated[to] = certificates;
            await store.SaveAsync(root, updated, cancellationToken);
        }
    }

    public static async Task RemoveAsync(IClientCertificateStore store, string root, string id, CancellationToken cancellationToken)
    {
        var stored = await store.LoadAsync(root, cancellationToken);
        if (stored.IsSuccess && stored.Value.ContainsKey(id))
        {
            await store.SaveAsync(
                root,
                stored.Value.Where(entry => entry.Key != id).ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
                cancellationToken);
        }
    }

    internal static ClientCertificateDto ToDto(ClientCertificateSetting setting) =>
        new(setting.Host, setting.Certificate, setting.Key.Length > 0 ? setting.Key : null, setting.Passphrase.Length > 0);
}

/// <summary>Lists the client certificates set up for an environment (without their passphrases).</summary>
public sealed class ListClientCertificates(IClientCertificateStore store, CurrentWorkspace current)
{
    public async Task<Result<IReadOnlyList<ClientCertificateDto>>> ExecuteAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!EnvironmentMapper.IsValidId(environmentId))
        {
            return EnvironmentMapper.InvalidId(environmentId);
        }

        var stored = await store.LoadAsync(root.Value, cancellationToken);
        return stored.Map<IReadOnlyList<ClientCertificateDto>>(all =>
            (all.GetValueOrDefault(environmentId) ?? []).Select(ClientCertificates.ToDto).ToList());
    }
}

/// <summary>
/// Replaces the client certificates of an environment. A certificate saved without a passphrase
/// (<c>null</c>) keeps the one stored for the certificate in the same place with the same file.
/// Rows without a certificate file are dropped.
/// </summary>
public sealed class SaveClientCertificates(IClientCertificateStore store, CurrentWorkspace current)
{
    public async Task<Result<IReadOnlyList<ClientCertificateDto>>> ExecuteAsync(
        string environmentId,
        IReadOnlyList<ClientCertificateDto> certificates,
        CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!EnvironmentMapper.IsValidId(environmentId))
        {
            return EnvironmentMapper.InvalidId(environmentId);
        }

        var stored = await store.LoadAsync(root.Value, cancellationToken);
        if (!stored.IsSuccess)
        {
            return stored.Error!;
        }

        var previous = stored.Value.GetValueOrDefault(environmentId) ?? [];
        var settings = new List<ClientCertificateSetting>();
        foreach (var (dto, index) in certificates.Select((dto, index) => (dto, index)))
        {
            var certificate = (dto.Certificate ?? string.Empty).Trim();
            if (certificate.Length == 0)
            {
                continue;
            }

            var host = (dto.Host ?? string.Empty).Trim();
            if (host.Any(char.IsWhiteSpace) || host.Contains('/'))
            {
                return new Error(ErrorCodes.InvalidRequest, $"'{host}' is not a host name: use api.example.com, *.example.com or host:port.");
            }

            var passphrase = dto.Passphrase
                ?? (index < previous.Count && previous[index].Certificate == certificate ? previous[index].Passphrase : string.Empty);
            settings.Add(new ClientCertificateSetting(host, certificate, (dto.Key ?? string.Empty).Trim(), passphrase));
        }

        var updated = stored.Value.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        updated[environmentId] = settings;
        var saved = await store.SaveAsync(root.Value, updated, cancellationToken);
        return saved.IsSuccess
            ? settings.Select(ClientCertificates.ToDto).ToList()
            : saved.Error!;
    }
}
