using Missive.Application.Common;
using Missive.Application.Updates;
using Velopack;
using Velopack.Sources;

namespace Missive.Infrastructure.Updates;

/// <summary>
/// Updates the app with Velopack from the releases of a GitHub repository. Only copies installed
/// by a Velopack installer (or run as a Linux AppImage) can update themselves.
/// </summary>
public sealed class VelopackAppUpdater : IAppUpdater
{
    /// <summary><c>null</c> when Velopack is not set up (<c>VelopackApp.Build().Run()</c> was not called).</summary>
    private readonly UpdateManager? _manager;
    private UpdateInfo? _found;

    /// <param name="repositoryUrl">The GitHub repository whose releases hold the update packages.</param>
    public VelopackAppUpdater(string repositoryUrl)
    {
        try
        {
            // Pre-release builds follow pre-releases too; stable builds only see stable releases.
            var manager = new UpdateManager(new GithubSource(repositoryUrl, accessToken: null, prerelease: false));
            _manager = manager.CurrentVersion?.IsPrerelease == true
                ? new UpdateManager(new GithubSource(repositoryUrl, accessToken: null, prerelease: true))
                : manager;
        }
        catch (InvalidOperationException)
        {
            // Velopack's locator is missing, so this copy cannot update itself.
            _manager = null;
        }
    }

    public bool IsSupported => _manager?.IsInstalled == true;

    public string? CurrentVersion => _manager?.CurrentVersion?.ToString();

    public async Task<Result<string?>> FindUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null || !_manager.IsInstalled)
        {
            return Result<string?>.Failure(ErrorCodes.UpdateFailed, "This copy of Missive cannot update itself.");
        }

        try
        {
            _found = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
            return _found?.TargetFullRelease.Version.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<string?>.Failure(ErrorCodes.UpdateFailed, $"Could not check for updates: {ex.Message}");
        }
    }

    public async Task<Result<Unit>> DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null || _found is null)
        {
            return Result<Unit>.Failure(ErrorCodes.UpdateFailed, "There is no update to download.");
        }

        try
        {
            await _manager.DownloadUpdatesAsync(_found, cancelToken: cancellationToken);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<Unit>.Failure(ErrorCodes.UpdateFailed, $"Could not download the update: {ex.Message}");
        }
    }

    public Result<Unit> RestartToUpdate()
    {
        if (_manager?.UpdatePendingRestart is not { } update)
        {
            return Result<Unit>.Failure(ErrorCodes.UpdateFailed, "No update has been downloaded.");
        }

        _manager.ApplyUpdatesAndRestart(update);
        return Unit.Value;
    }
}
