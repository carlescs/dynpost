using Missive.Application.Common;
using Missive.Application.Requests;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;

namespace Missive.Application.Git;

/// <summary>
/// Both sides of a changed file parsed as requests, so the UI can compare them field by field. Only
/// request files of the workspace are parsed; a side that can't be parsed leaves the request null.
/// </summary>
internal sealed record GitRequestSides(bool IsRequest, RequestDto? OriginalRequest, RequestDto? ModifiedRequest, string? RequestError)
{
    public static GitRequestSides NotRequest { get; } = new(false, null, null, null);

    /// <param name="path">The file's path in the working tree or in the commit.</param>
    /// <param name="originalPath">The path the file had in the older version, for a rename.</param>
    /// <param name="original">The older text, or <c>null</c> when the file didn't exist there.</param>
    /// <param name="modified">The newer text, or <c>null</c> when the file doesn't exist there.</param>
    public static GitRequestSides Parse(
        IRequestFileFormat format,
        string path,
        string? originalPath,
        string? original,
        string? modified)
    {
        if (!WorkspacePath.TryParse(path, out var modifiedPath) || !WorkspaceLayout.IsRequestPath(modifiedPath))
        {
            return NotRequest;
        }

        var originalWorkspacePath = originalPath is { } from && WorkspacePath.TryParse(from, out var parsed) ? parsed : modifiedPath;
        var originalResult = original is null ? null : (Result<HttpRequestDefinition>?)format.Parse(original, originalWorkspacePath);
        var modifiedResult = modified is null ? null : (Result<HttpRequestDefinition>?)format.Parse(modified, modifiedPath);

        return new GitRequestSides(
            IsRequest: true,
            OriginalRequest: ToDto(originalResult),
            ModifiedRequest: ToDto(modifiedResult),
            RequestError: originalResult?.Error?.Message ?? modifiedResult?.Error?.Message);
    }

    private static RequestDto? ToDto(Result<HttpRequestDefinition>? result) =>
        result is { IsSuccess: true } ? RequestMapper.ToDto(result.Value.Value) : null;
}
