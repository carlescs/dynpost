using Missive.Application.Common;
using Missive.Application.Requests;
using Missive.Application.Workspaces;
using Missive.Domain.Git;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;

namespace Missive.Application.Git;

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
        var dto = new GitFileDiffDto(
            diff.Path,
            GitStatusDto.StatusName(diff.Status),
            diff.OriginalPath,
            diff.IsBinary,
            diff.Original,
            diff.Modified,
            IsRequest: false);

        if (diff.IsBinary
            || !WorkspacePath.TryParse(diff.Path, out var path)
            || !WorkspaceLayout.IsRequestPath(path))
        {
            return dto;
        }

        var originalPath = diff.OriginalPath is { } from && WorkspacePath.TryParse(from, out var parsed) ? parsed : path;
        var original = Parse(diff.Original, originalPath);
        var modified = Parse(diff.Modified, path);

        return dto with
        {
            IsRequest = true,
            OriginalRequest = original?.IsSuccess == true ? RequestMapper.ToDto(original.Value.Value) : null,
            ModifiedRequest = modified?.IsSuccess == true ? RequestMapper.ToDto(modified.Value.Value) : null,
            RequestError = original?.Error?.Message ?? modified?.Error?.Message,
        };
    }

    private Result<HttpRequestDefinition>? Parse(string? text, WorkspacePath path) =>
        text is null ? null : (Result<HttpRequestDefinition>?)requestFormat.Parse(text, path);
}
