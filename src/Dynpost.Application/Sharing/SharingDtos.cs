namespace Dynpost.Application.Sharing;

/// <param name="Path">The workspace-relative path of the file.</param>
/// <param name="LocalOnly">Whether the file is kept on this computer only (never committed).</param>
public sealed record SharingDto(string Path, bool LocalOnly);
