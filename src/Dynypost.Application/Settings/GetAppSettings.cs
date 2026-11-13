using Dynypost.Application.Common;

namespace Dynypost.Application.Settings;

/// <summary>Reads the settings the UI shows.</summary>
public sealed class GetAppSettings(IAppSettingsStore store)
{
    public async Task<Result<AppSettingsDto>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        AppSettingsDto.From(await store.LoadAsync(cancellationToken));
}
