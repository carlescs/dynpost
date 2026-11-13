using Dynypost.Application.Common;
using Dynypost.Application.Requests;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Git;

namespace Dynypost.Application.Git;

/// <summary>
/// Reads what changed in one file of the open workspace since the last commit. Request files are
/// also parsed on both sides, so the UI can compare them field by field.
/// </summary>
public sealed class GetFileDiff(IGitService git, IRequestFileFormat requestFormat, CurrentWorkspace workspace)
{
    public async Task<Result<GitFileDiffDto>> ExecuteAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        var status = await git.GetStatusAsync(root.Value, cancellationToken);
        if (status.IsFailure)
        {
            return status.Error!;
        }

        // Only files git reports as changed can be read, so the path can't point elsewhere.
        var change = status.Value.Changes.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.Ordinal));
        if (change is null)
        {
            return GitErrors.FileNotChanged(path);
        }

        var diff = await git.GetFileDiffAsync(root.Value, change, cancellationToken);
        if (diff.IsFailure)
        {
            return diff.Error!;
        }

        return ToDto(diff.Value);
    }

    private GitFileDiffDto ToDto(GitFileDiff diff)
    {
        var requests = diff.IsBinary
            ? GitRequestSides.NotRequest
            : GitRequestSides.Parse(requestFormat, diff.Path, diff.OriginalPath, diff.Original, diff.Modified);

        return new GitFileDiffDto(
            diff.Path,
            GitStatusDto.StatusName(diff.Status),
            diff.OriginalPath,
            diff.IsBinary,
            diff.Original,
            diff.Modified,
            requests.IsRequest,
            requests.OriginalRequest,
            requests.ModifiedRequest,
            requests.RequestError);
    }
}
