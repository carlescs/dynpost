using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Folders;
using Dynpost.Application.Requests;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Environments;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Interchange;

/// <param name="Source">The full path of the file to import, chosen by the user.</param>
/// <param name="Folder">The folder to import into: <c>requests</c> or a folder inside it.</param>
public sealed record ImportCollectionRequest(string Source, string Folder = WorkspaceLayout.RequestsFolder);

/// <summary>
/// Imports a file exported by another app (Postman, Insomnia, a HAR capture, an OpenAPI
/// description…) into the open workspace. Its requests go into a new folder named after the
/// collection, as ordinary request files; its environments become environment files. Nothing that
/// already exists is changed: names that are taken get a number.
/// </summary>
public sealed class ImportCollection(
    IWorkspaceRepository repository,
    IEnvironmentStore environments,
    ISecretStore secrets,
    IExternalFiles files,
    ICollectionImporter importer,
    CurrentWorkspace current)
{
    private const int MaxAttempts = 1000;

    public async Task<Result<ImportResultDto>> ExecuteAsync(ImportCollectionRequest request, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, request.Folder, allowRoot: true, out var parent, out var folderError))
        {
            return folderError;
        }

        var text = await files.ReadTextAsync(request.Source, cancellationToken);
        if (!text.IsSuccess)
        {
            return text.Error!;
        }

        var parsed = importer.Parse(text.Value, Path.GetFileName(request.Source));
        if (!parsed.IsSuccess)
        {
            return parsed.Error!;
        }

        var collection = parsed.Value.Collection;
        if (collection.Root.IsEmpty && collection.Environments.Count == 0)
        {
            return new Error(ErrorCodes.InvalidFile, $"The {parsed.Value.Format} has no requests or environments to import.");
        }

        // Check the secrets can be stored before writing anything.
        var stored = await secrets.LoadAsync(root.Value, cancellationToken);
        if (!stored.IsSuccess)
        {
            return stored.Error!;
        }

        var warnings = parsed.Value.Warnings.ToList();
        var counts = new Counts();
        string? folderPath = null;
        if (!collection.Root.IsEmpty)
        {
            var target = FreeFolder(root.Value, parent, collection.Root.Name);
            if (target is null)
            {
                return new Error(ErrorCodes.Io, $"Could not find a free folder name for '{collection.Root.Name}'.");
            }

            var created = await WriteFolderAsync(root.Value, target, collection.Root, warnings, counts, cancellationToken);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            folderPath = target.Value;
        }

        var ids = new List<string>();
        var secretValues = new Dictionary<string, IReadOnlyDictionary<string, string>>(stored.Value, StringComparer.Ordinal);
        foreach (var imported in collection.Environments)
        {
            var environment = ImportSanitizer.Environment(imported, warnings);
            var id = EnvironmentFiles.FreeId(environments, root.Value, environment.Name, current: null);
            if (id is null)
            {
                warnings.Add($"Skipped the environment '{environment.Name}': no free file name was found.");
                continue;
            }

            var written = await environments.WriteAsync(root.Value, id, environment, cancellationToken);
            if (!written.IsSuccess)
            {
                return written.Error!;
            }

            ids.Add(id);
            var values = EnvironmentMapper.SecretValues(environment);
            if (values.Count > 0)
            {
                secretValues[id] = values;
            }
        }

        if (secretValues.Count != stored.Value.Count)
        {
            var saved = await secrets.SaveAsync(root.Value, secretValues, cancellationToken);
            if (!saved.IsSuccess)
            {
                return saved.Error!;
            }
        }

        return new ImportResultDto(parsed.Value.Format, folderPath, counts.Requests, counts.Folders, ids, warnings);
    }

    private async Task<Result<Unit>> WriteFolderAsync(
        string root,
        WorkspacePath path,
        CollectionFolder folder,
        List<string> warnings,
        Counts counts,
        CancellationToken cancellationToken)
    {
        var created = repository.CreateFolder(root, path);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        counts.Folders++;
        foreach (var imported in folder.Requests)
        {
            var request = ImportSanitizer.Request(imported, warnings);
            if (request is null)
            {
                continue;
            }

            var file = CreateRequest.FreePath(repository, root, path, request.Name, current: null);
            if (file is null)
            {
                warnings.Add($"Skipped the request '{request.Name}': no free file name was found.");
                continue;
            }

            var written = await repository.WriteRequestAsync(root, file, request, cancellationToken);
            if (!written.IsSuccess)
            {
                return written.Error!;
            }

            counts.Requests++;
        }

        foreach (var child in folder.Folders)
        {
            var childPath = FreeFolder(root, path, child.Name);
            if (childPath is null)
            {
                warnings.Add($"Skipped the folder '{child.Name}': no free folder name was found.");
                continue;
            }

            var written = await WriteFolderAsync(root, childPath, child, warnings, counts, cancellationToken);
            if (!written.IsSuccess)
            {
                return written;
            }
        }

        return Unit.Value;
    }

    /// <summary>A new folder in <paramref name="parent"/> for <paramref name="name"/>: <c>Users</c>, else <c>Users 2</c>, …</summary>
    private WorkspacePath? FreeFolder(string root, WorkspacePath parent, string name)
    {
        var baseName = WorkspaceLayout.ToFolderName(name, "Imported");
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var candidate = attempt == 1 ? baseName : $"{baseName} {attempt}";
            if (WorkspaceLayout.ValidateFolderName(parent, candidate) is null)
            {
                var path = parent.Combine(candidate);
                if (!repository.EntryExists(root, path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private sealed class Counts
    {
        public int Requests { get; set; }

        public int Folders { get; set; }
    }
}
