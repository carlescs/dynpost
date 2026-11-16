using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;

namespace Dynpost.Application.Git;

/// <summary>What to clone and where.</summary>
/// <param name="Url">The repository's URL, as git accepts it.</param>
/// <param name="ParentFolder">The existing folder the clone is created in.</param>
/// <param name="Name">
/// The new folder's name; when omitted, the repository's name taken from the URL, as git would.
/// </param>
public sealed record CloneRepositoryRequest(string Url, string ParentFolder, string? Name = null);

/// <summary>
/// Clones a remote repository into a new folder and opens that folder as the workspace, using the
/// user's own git configuration and credentials.
/// </summary>
public sealed class CloneRepository(IGitService git, IWorkspaceRepository repository, OpenWorkspace open)
{
    public async Task<Result<WorkspaceDto>> ExecuteAsync(CloneRepositoryRequest request, CancellationToken cancellationToken = default)
    {
        if (!RemoteUrl.TryCreate(request.Url, out var url, out var urlError))
        {
            return GitErrors.InvalidRemote(urlError);
        }

        if (string.IsNullOrWhiteSpace(request.ParentFolder) || !repository.FolderExists(request.ParentFolder))
        {
            return new Error(ErrorCodes.NotFound, $"The folder '{request.ParentFolder}' does not exist.");
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? url.RepositoryName : request.Name.Trim();
        var nameError = CheckFolderName(name);
        if (nameError is not null)
        {
            return GitErrors.InvalidCloneFolder(nameError);
        }

        var destination = Path.Combine(request.ParentFolder, name!);
        var clone = await git.CloneAsync(url, destination, cancellationToken);
        if (clone.IsFailure)
        {
            return clone.Error!;
        }

        return await open.ExecuteAsync(destination, cancellationToken);
    }

    /// <summary>Why <paramref name="name"/> can't name the new folder, or null when it can.</summary>
    private static string? CheckFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter a name for the new folder.";
        }

        if (name is "." or ".." || name.IndexOfAny(['/', '\\']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return $"'{name}' can't be used as a folder name.";
        }

        return null;
    }
}
