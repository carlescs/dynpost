using Dynpost.Application.Common;

namespace Dynpost.Application.Workspaces;

/// <summary>Opens folders in the operating system's file manager.</summary>
public interface IFileManager
{
    /// <summary>
    /// Shows <paramref name="folder"/> in the file manager (File Explorer on Windows, Finder on
    /// macOS, the desktop's default on Linux). Fails when no file manager could be started.
    /// </summary>
    Result<Unit> ShowFolder(string folder);
}

/// <summary>Shows the open workspace folder in the operating system's file manager.</summary>
public sealed class ShowWorkspaceFolder(CurrentWorkspace workspace, IWorkspaceRepository repository, IFileManager fileManager)
{
    public Result<Unit> Execute()
    {
        var root = workspace.RequireRoot();
        if (root.IsFailure)
        {
            return root.Error!;
        }

        if (!repository.FolderExists(root.Value!))
        {
            return new Error(ErrorCodes.NotFound, $"The workspace folder '{root.Value}' no longer exists.");
        }

        return fileManager.ShowFolder(root.Value!);
    }
}
