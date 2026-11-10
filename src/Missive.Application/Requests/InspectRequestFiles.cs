using Missive.Application.Interchange;
using Missive.Application.Workspaces;
using Missive.Domain.Http;

namespace Missive.Application.Requests;

/// <summary>A file picked for a request body: workspace-relative when it is inside the workspace.</summary>
public sealed record PickedRequestFileDto(string Path, bool IsAbsolute);

/// <summary>What a request body's file path points at.</summary>
/// <param name="Path">The path as written in the request.</param>
/// <param name="Size">The file's size in bytes; <c>null</c> when it doesn't exist.</param>
/// <param name="Error">Why the path can't be sent (outside the workspace, no workspace open…); <c>null</c> when it can.</param>
public sealed record RequestFileInfoDto(string Path, bool IsAbsolute, bool Exists, long? Size, string? Error = null);

/// <summary>
/// Describes the files request bodies send, so the editor can show their size and warn about
/// missing files, files outside the workspace and absolute paths before sending.
/// </summary>
public sealed class InspectRequestFiles(IExternalFiles files, CurrentWorkspace current)
{
    /// <summary>The paths to store for files the user picked (absolute paths from the file dialog).</summary>
    public IReadOnlyList<PickedRequestFileDto> Picked(IEnumerable<string> pickedPaths) =>
        pickedPaths
            .Select(path => RequestFilePaths.ForRequest(path, current.Root))
            .Select(picked => new PickedRequestFileDto(picked.Path, picked.IsAbsolute))
            .ToList();

    /// <summary>Looks up each path as a request would resolve it when sent.</summary>
    public IReadOnlyList<RequestFileInfoDto> Execute(IEnumerable<string> paths) =>
        paths
            .Distinct(StringComparer.Ordinal)
            .Select(path =>
            {
                var trimmed = path.Trim();
                var isAbsolute = trimmed.Length > 0 && System.IO.Path.IsPathRooted(trimmed);
                if (trimmed.Length == 0)
                {
                    return new RequestFileInfoDto(path, false, false, null, "Choose a file.");
                }

                var resolved = RequestFilePaths.Resolve(trimmed, current.Root);
                if (resolved.FullPath is null)
                {
                    return new RequestFileInfoDto(path, isAbsolute, false, null, resolved.Error);
                }

                var size = files.FileSize(resolved.FullPath);
                return new RequestFileInfoDto(path, isAbsolute, size is not null, size);
            })
            .ToList();
}
