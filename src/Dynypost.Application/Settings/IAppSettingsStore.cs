using Dynypost.Application.Common;

namespace Dynypost.Application.Settings;

/// <summary>Stores the per-user <see cref="AppSettings"/>.</summary>
public interface IAppSettingsStore
{
    /// <summary>Loads the settings; missing or unreadable settings load as <see cref="AppSettings.Default"/>.</summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task<Result<Unit>> SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task<Result<AppSettings>> UpdateAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken = default);
}
