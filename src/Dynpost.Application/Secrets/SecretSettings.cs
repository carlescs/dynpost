using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Settings;
using Dynpost.Application.Workspaces;

namespace Dynpost.Application.Secrets;

/// <summary>Where secret variable values are kept.</summary>
public static class SecretStorages
{
    /// <summary>In the workspace's git-ignored <c>.dynpost/secrets.yaml</c> (the default).</summary>
    public const string File = "file";

    /// <summary>In the operating system's credential store (see <see cref="IKeychain"/>).</summary>
    public const string Keychain = "keychain";

    public static bool IsValid(string? storage) => storage is File or Keychain;
}

/// <summary>Per-user settings for secret values.</summary>
/// <param name="Storage"><see cref="SecretStorages.File"/> or <see cref="SecretStorages.Keychain"/>.</param>
/// <param name="UseDotEnv">Whether secrets without a value on this computer take one from the workspace's <c>.env</c> file.</param>
public sealed record SecretSettings(string Storage = SecretStorages.File, bool UseDotEnv = false)
{
    public static SecretSettings Default { get; } = new();
}

/// <summary>The operating system's credential store, holding one text value per account name.</summary>
public interface IKeychain
{
    /// <summary>Whether it can be used on this computer (for example, libsecret is installed and running on Linux).</summary>
    bool IsAvailable { get; }

    /// <summary>What it is called on this system, e.g. <c>Windows Credential Manager</c>.</summary>
    string Name { get; }

    /// <summary>The value stored for <paramref name="account"/>; <c>null</c> when there is none.</summary>
    Task<Result<string?>> GetAsync(string account, CancellationToken cancellationToken = default);

    Task<Result<Unit>> SetAsync(string account, string value, CancellationToken cancellationToken = default);

    /// <summary>Deletes the value of <paramref name="account"/>; nothing happens when there is none.</summary>
    Task<Result<Unit>> DeleteAsync(string account, CancellationToken cancellationToken = default);
}

/// <summary>
/// The variables of the workspace's <c>.env</c> file (<c>NAME=value</c> lines), used as the values
/// of secrets that have none on this computer, when that is turned on in the
/// <see cref="SecretSettings"/>.
/// </summary>
public interface IDotEnvValues
{
    /// <summary>The <c>.env</c> values, or none when the file is missing or reading it is turned off.</summary>
    Task<IReadOnlyDictionary<string, string>> ReadAsync(string root, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies the <c>.env</c> file of <paramref name="fromRoot"/> to <paramref name="toRoot"/>, whatever
    /// the setting, unless the destination has one already. Returns whether it copied.
    /// </summary>
    Task<Result<bool>> CopyAsync(string fromRoot, string toRoot, CancellationToken cancellationToken = default);
}

/// <summary>The secret settings as the UI shows them.</summary>
/// <param name="KeychainAvailable">Whether the operating system's credential store can be used here.</param>
/// <param name="KeychainName">What the credential store is called on this system.</param>
public sealed record SecretSettingsDto(string Storage, bool UseDotEnv, bool KeychainAvailable, string KeychainName);

/// <summary>Reads where secret values are kept and whether <c>.env</c> values are used.</summary>
public sealed class GetSecretSettings(IAppSettingsStore store, IKeychain keychain)
{
    public async Task<Result<SecretSettingsDto>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var settings = (await store.LoadAsync(cancellationToken)).Secrets ?? SecretSettings.Default;
        return ToDto(settings, keychain);
    }

    internal static SecretSettingsDto ToDto(SecretSettings settings, IKeychain keychain) =>
        new(
            SecretStorages.IsValid(settings.Storage) ? settings.Storage : SecretStorages.File,
            settings.UseDotEnv,
            keychain.IsAvailable,
            keychain.Name);
}

/// <summary>
/// Changes where secret values are kept and whether <c>.env</c> values are used. The open
/// workspace's secrets move to the new place right away (through <see cref="ISecretStore"/>, which
/// moves each workspace's secrets the first time it reads them after the change).
/// </summary>
public sealed class SetSecretSettings(IAppSettingsStore store, IKeychain keychain, ISecretStore secrets, CurrentWorkspace current)
{
    public const string InvalidSettingsCode = "settings.invalid_secrets";

    public async Task<Result<SecretSettingsDto>> ExecuteAsync(string storage, bool useDotEnv, CancellationToken cancellationToken = default)
    {
        if (!SecretStorages.IsValid(storage))
        {
            return new Error(InvalidSettingsCode, $"'{storage}' is not a place for secrets (use file or keychain).");
        }

        if (storage == SecretStorages.Keychain && !keychain.IsAvailable)
        {
            return new Error(InvalidSettingsCode, $"{keychain.Name} can't be used on this computer.");
        }

        var settings = new SecretSettings(storage, useDotEnv);
        var saved = await store.UpdateAsync(currentSettings => currentSettings with { Secrets = settings }, cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved.Error!;
        }

        if (current.Root is { } root)
        {
            // Reading the secrets moves them to the new place.
            var moved = await secrets.LoadAsync(root, cancellationToken);
            if (!moved.IsSuccess)
            {
                return moved.Error!;
            }
        }

        return GetSecretSettings.ToDto(settings, keychain);
    }
}
