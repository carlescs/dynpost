using Missive.Application.Common;
using Missive.Domain.Workspaces;

namespace Missive.Application.Sharing;

/// <summary>
/// The workspace files (requests, environments, run plans) that are kept on this computer instead
/// of being shared through git. Implementations record them where git sees them, so they are never
/// committed, and keep the record in step when such files are moved, copied or deleted.
/// </summary>
public interface ILocalOnlyFiles
{
    /// <summary>Every local-only file; a workspace without any has an empty set.</summary>
    Result<IReadOnlySet<WorkspacePath>> List(string root);

    /// <summary>Marks one file as local-only or shared. Returns whether anything changed.</summary>
    Result<bool> Set(string root, WorkspacePath path, bool localOnly);

    /// <summary>
    /// Follows a rename or move: local-only files at <paramref name="from"/>, or inside it when it
    /// is a folder, become local-only at the matching path under <paramref name="to"/>.
    /// </summary>
    Result<Unit> Move(string root, WorkspacePath from, WorkspacePath to);

    /// <summary>
    /// Follows a copy: the copies of local-only files at or inside <paramref name="from"/> are
    /// local-only too, so duplicating a private file never shares it.
    /// </summary>
    Result<Unit> Copy(string root, WorkspacePath from, WorkspacePath to);

    /// <summary>Forgets the local-only files at or inside <paramref name="path"/>, after they were deleted.</summary>
    Result<Unit> Remove(string root, WorkspacePath path);
}
