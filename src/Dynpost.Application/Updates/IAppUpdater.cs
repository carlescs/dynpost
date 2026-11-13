using Dynpost.Application.Common;

namespace Dynpost.Application.Updates;

/// <summary>Finds, downloads and installs new versions of the app itself.</summary>
public interface IAppUpdater
{
    /// <summary>
    /// Whether this copy can update itself: only installed copies can. A copy run from a build
    /// folder or an extracted archive cannot.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>The running version, when known.</summary>
    string? CurrentVersion { get; }

    /// <summary>Looks for a newer version; the result is its version, or <c>null</c> when up to date.</summary>
    Task<Result<string?>> FindUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads the update the last <see cref="FindUpdateAsync"/> found, ready to install.</summary>
    Task<Result<Unit>> DownloadUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs the downloaded update and restarts the app. On success the process exits, so this
    /// only returns when there is nothing to install.
    /// </summary>
    Result<Unit> RestartToUpdate();
}
