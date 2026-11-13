using Dynypost.Application.Common;
using Dynypost.Application.Sharing;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Workspaces;

namespace Dynypost.Application.Folders;

/// <summary>
/// Copies a collection folder, with everything in it, next to the original as <c>… copy</c>
/// (<c>… copy 2</c>, <c>… copy 3</c>, … when that name is taken). Files are copied byte for byte
/// and only new files are written. Folders holding symbolic links, or names that aren't valid
/// workspace paths, are refused rather than copied incompletely.
/// </summary>
public sealed class DuplicateFolder(IWorkspaceRepository repository, ILocalOnlyFiles localFiles, CurrentWorkspace current)
{
    private const int MaxAttempts = 1000;
    private const int MaxListed = 3;

    /// <summary>
    /// Bridge messages are handled concurrently; one duplicate at a time means two copies never
    /// pick the same destination, so a failed copy only ever rolls back its own folder.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public Result<FolderDto> Execute(string path, WorkspacePath? area = null)
    {
        Gate.Wait();
        try
        {
            return Duplicate(path, area);
        }
        finally
        {
            Gate.Release();
        }
    }

    private Result<FolderDto> Duplicate(string path, WorkspacePath? area)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, path, allowRoot: false, out var folder, out var error, area))
        {
            return error;
        }

        var contents = repository.ReadFolderContents(root.Value, folder);
        if (!contents.IsSuccess)
        {
            return contents.Error!;
        }

        var skipped = contents.Value.Links.Select(l => l.Value)
            .Concat((contents.Value.Unsupported ?? []).Select(u => $"{folder}/{u}"))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (skipped.Count > 0)
        {
            var listed = string.Join(", ", skipped.Take(MaxListed));
            var more = skipped.Count > MaxListed ? $" and {skipped.Count - MaxListed} more" : string.Empty;
            return new Error(
                ErrorCodes.InvalidPath,
                $"'{folder}' contains links or names Dynypost can't copy ({listed}{more}). Move or rename them first.");
        }

        var target = FreeCopyPath(root.Value, folder);
        if (!target.IsSuccess)
        {
            return target.Error!;
        }

        var copied = Copy(root.Value, folder, target.Value, contents.Value);
        if (!copied.IsSuccess)
        {
            // Everything under the target was created just now, so it is safe to remove.
            repository.DeleteFolder(root.Value, target.Value);
            return copied.Error!;
        }

        // Copies of local-only requests are local-only too.
        var followed = localFiles.Copy(root.Value, folder, target.Value);
        return followed.IsSuccess ? new FolderDto(target.Value.Value) : followed.Error!;
    }

    private Result<WorkspacePath> FreeCopyPath(string root, WorkspacePath folder)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var name = folder.FileName + " copy" + (attempt > 1 ? $" {attempt}" : string.Empty);
            var candidate = FolderPaths.Child(folder.Parent, name);
            if (!candidate.IsSuccess)
            {
                return candidate.Error!;
            }

            if (!repository.EntryExists(root, candidate.Value))
            {
                return candidate.Value;
            }
        }

        return new Error(ErrorCodes.Io, $"Could not find a free name for a copy of '{folder}'.");
    }

    private Result<Unit> Copy(string root, WorkspacePath from, WorkspacePath to, FolderContents contents)
    {
        WorkspacePath Map(WorkspacePath path) => WorkspacePath.Parse(to.Value + path.Value[from.Value.Length..]);

        var created = repository.CreateFolder(root, to);
        if (!created.IsSuccess)
        {
            return created;
        }

        // Parents come before their children, so each folder's parent already exists.
        foreach (var folder in contents.Folders.OrderBy(f => f.Value.Length))
        {
            created = repository.CreateFolder(root, Map(folder));
            if (!created.IsSuccess)
            {
                return created;
            }
        }

        foreach (var file in contents.Files)
        {
            var copied = repository.CopyFile(root, file, Map(file));
            if (!copied.IsSuccess)
            {
                return copied;
            }
        }

        return Unit.Value;
    }
}
