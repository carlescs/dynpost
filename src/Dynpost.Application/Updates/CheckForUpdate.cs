using Dynpost.Application.Common;

namespace Dynpost.Application.Updates;

/// <summary>
/// Looks for a newer version of the app and downloads it, so that it installs on the next restart
/// (<see cref="RestartToUpdate"/>). Nothing is installed without the user's action.
/// </summary>
public sealed class CheckForUpdate(IAppUpdater updater)
{
    public async Task<Result<UpdateStatusDto>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!updater.IsSupported)
        {
            return new UpdateStatusDto(UpdateStatusDto.Unsupported, updater.CurrentVersion);
        }

        var found = await updater.FindUpdateAsync(cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        if (found.Value is not { } version)
        {
            return new UpdateStatusDto(UpdateStatusDto.UpToDate, updater.CurrentVersion);
        }

        var downloaded = await updater.DownloadUpdateAsync(cancellationToken);
        return downloaded.IsSuccess
            ? new UpdateStatusDto(UpdateStatusDto.Ready, updater.CurrentVersion, version)
            : downloaded.Error!;
    }
}
