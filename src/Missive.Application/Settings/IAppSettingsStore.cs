using Missive.Application.Common;

namespace Missive.Application.Settings;

/// <summary>Stores the per-user <see cref="AppSettings"/>.</summary>
public interface IAppSettingsStore
{
    /// <summary>Loads the settings; missing or unreadable settings load as <see cref="AppSettings.Default"/>.</summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task<Result<Unit>> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
