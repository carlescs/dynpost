using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Search;

/// <summary>The request files of a workspace, as quick open and workspace search go through them.</summary>
internal static class RequestFiles
{
    /// <summary>A request file and what is known about it.</summary>
    /// <param name="Request">The parsed request; <c>null</c> when the file could not be read.</param>
    public sealed record Item(WorkspacePath Path, HttpRequestDefinition? Request)
    {
        public string Name => Request?.Name ?? FileStem(Path);

        public string? Method => Request?.Method.ToName();

        private static string FileStem(WorkspacePath path) =>
            path.FileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ? path.FileName[..^5] : path.FileName;
    }

    /// <summary>Every request file in <paramref name="entries"/>, in the tree's order.</summary>
    public static IEnumerable<Item> Flatten(IEnumerable<WorkspaceEntry> entries)
    {
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case FolderEntry folder:
                    foreach (var item in Flatten(folder.Children))
                    {
                        yield return item;
                    }

                    break;
                case RequestFileEntry file:
                    yield return new Item(file.Path, file.Request.IsSuccess ? file.Request.Value : null);
                    break;
            }
        }
    }

    /// <summary>Lists the request files of the workspace at <paramref name="root"/>.</summary>
    public static async Task<Result<IReadOnlyList<Item>>> ListAsync(
        IWorkspaceRepository repository,
        string root,
        CancellationToken cancellationToken)
    {
        var entries = await repository.ListAsync(root, cancellationToken);
        return entries.IsSuccess ? Flatten(entries.Value).ToList() : entries.Error!;
    }
}
