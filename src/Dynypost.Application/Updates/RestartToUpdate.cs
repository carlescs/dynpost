using Dynypost.Application.Common;

namespace Dynypost.Application.Updates;

/// <summary>Installs the update <see cref="CheckForUpdate"/> downloaded and restarts the app.</summary>
public sealed class RestartToUpdate(IAppUpdater updater)
{
    public Result<Unit> Execute() =>
        updater.IsSupported
            ? updater.RestartToUpdate()
            : Result<Unit>.Failure(ErrorCodes.UpdateFailed, "This copy of Dynypost was not installed, so it cannot update itself.");
}
