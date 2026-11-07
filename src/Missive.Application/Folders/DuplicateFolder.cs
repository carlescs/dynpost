using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;

namespace Missive.Application.Folders;

/// <summary>
/// Copies a collection folder, with everything in it, next to the original as <c>… copy</c>
/// (<c>… copy 2</c>, <c>… copy 3</c>, … when that name is taken). Files are copied byte for byte
/// and only new files are written; symbolic links are never followed or copied.
/// </summary>
public sealed class DuplicateFolder(IWorkspaceRepository repository, CurrentWorkspace current)
{
    private const int MaxAttempts = 1000;

    public Result<FolderDto> Execute(string path)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, path, allowRoot: false, out var folder, out var error))
        {
            return error;
        }

        var contents = repository.ReadFolderContents(root.Value, folder);
        if (!contents.IsSuccess)
        {
            return contents.Error!;
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

        return new FolderDto(target.Value.Value);
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
