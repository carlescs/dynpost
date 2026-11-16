using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Folders;
using Dynpost.Application.Requests;
using Dynpost.Application.Runs;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Environments;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Runs;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Interchange;

/// <param name="Source">The full path of the file to import, chosen by the user.</param>
/// <param name="Folder">The folder to import into: <c>requests</c> or a folder inside it.</param>
public sealed record ImportCollectionRequest(string Source, string Folder = WorkspaceLayout.RequestsFolder);

/// <summary>
/// Imports a file exported by another app (Postman, Insomnia, a HAR capture, an OpenAPI
/// description…) into the open workspace. Its requests go into a new folder named after the
/// collection, as ordinary request files; its environments become environment files. Nothing that
/// already exists is changed: names that are taken get a number. Folder settings (headers, auth,
/// variables) are written to the new folders' <c>_folder.yaml</c>. Load run plans (JMeter thread
/// groups) go into a new folder of <c>plans/</c> named after the collection, and the CSV files they
/// read are copied into a new folder of <c>data/</c>.
/// </summary>
public sealed class ImportCollection(
    IWorkspaceRepository repository,
    IFolderDefaultsStore folderDefaults,
    IRunPlanStore plans,
    IRunPlanFormat planFormat,
    IEnvironmentStore environments,
    ISecretStore secrets,
    IExternalFiles files,
    ICollectionImporter importer,
    CurrentWorkspace current)
{
    private const int MaxAttempts = 1000;

    /// <summary>The folder CSV files of imported run plans are copied into.</summary>
    public const string DataFolder = "data";

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

        var planPaths = await WritePlansAsync(root.Value, request.Source, collection, counts.Paths, warnings, cancellationToken);
        if (!planPaths.IsSuccess)
        {
            return planPaths.Error!;
        }

        return new ImportResultDto(parsed.Value.Format, folderPath, counts.Requests, counts.Folders, ids, warnings)
        {
            Plans = planPaths.Value,
        };
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
        if (!folder.Defaults.IsEmpty && ImportSanitizer.Folder(folder.Defaults, folder.Name, warnings) is { } defaults)
        {
            var saved = await folderDefaults.WriteAsync(root, path, defaults, cancellationToken);
            if (!saved.IsSuccess)
            {
                return saved.Error!;
            }
        }

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
            counts.Paths[imported] = file;
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

    /// <summary>
    /// Writes the collection's run plans, with the paths its requests were written to. A plan that
    /// can't be saved (none of its requests was imported, or it isn't valid) is skipped with a warning.
    /// </summary>
    private async Task<Result<IReadOnlyList<string>>> WritePlansAsync(
        string root,
        string source,
        RequestCollection collection,
        IReadOnlyDictionary<HttpRequestDefinition, WorkspacePath> paths,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var written = new List<string>();
        if (collection.Plans.Count == 0)
        {
            return written;
        }

        WorkspacePath? folder = null;
        var copies = new DataCopies(files, path => repository.EntryExists(root, path), () => FreeFolder(root, WorkspacePath.Root.Combine(DataFolder), collection.Root.Name), root, source);
        foreach (var imported in collection.Plans)
        {
            var name = ImportSanitizer.PlanName(imported.Name);
            var requests = imported.Requests.Where(r => paths.ContainsKey(r.Request)).ToList();
            if (requests.Count == 0)
            {
                warnings.Add($"Skipped the run plan '{name}': none of its requests was imported.");
                continue;
            }

            if (requests.Count < imported.Requests.Count)
            {
                warnings.Add($"The run plan '{name}' leaves out {imported.Requests.Count - requests.Count} request(s) that were not imported.");
            }

            var assertions = new Dictionary<string, RunAssertion>(StringComparer.Ordinal);
            if (imported.Assertions is { IsEmpty: false } all)
            {
                assertions[RunPlan.AllRequests] = all;
            }

            foreach (var request in requests.Where(r => r.Assertion is { IsEmpty: false }))
            {
                assertions[paths[request.Request].Value] = request.Assertion!;
            }

            var data = imported.Data is null ? null : await copies.CopyAsync(imported.Data, name, warnings, cancellationToken);
            var plan = new RunPlan(
                name,
                requests.Select(r => paths[r.Request]).ToList(),
                imported.Load,
                data,
                assertions,
                RunThresholds.None,
                imported.Order,
                requests.Where(r => r.Weight is not null).ToDictionary(r => paths[r.Request].Value, r => r.Weight!.Value, StringComparer.Ordinal),
                requests.Where(r => !r.Enabled).Select(r => paths[r.Request].Value).ToList());

            var errors = plan.Validate();
            if (errors.Count > 0)
            {
                warnings.Add($"Skipped the run plan '{name}': {string.Join(" ", errors)}");
                continue;
            }

            folder ??= FreeFolder(root, WorkspaceLayout.PlansRoot, collection.Root.Name);
            if (folder is null)
            {
                warnings.Add($"Skipped the run plans: no free folder name was found in '{WorkspaceLayout.PlansFolder}/'.");
                break;
            }

            var path = FreePlanPath(root, folder, name);
            if (path is null)
            {
                warnings.Add($"Skipped the run plan '{name}': no free file name was found.");
                continue;
            }

            var saved = await plans.WriteAsync(root, path, planFormat.Write(plan), cancellationToken);
            if (!saved.IsSuccess)
            {
                return saved.Error!;
            }

            written.Add(path.Value);
        }

        return written;
    }

    private WorkspacePath? FreePlanPath(string root, WorkspacePath folder, string name)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var path = folder.Combine(WorkspaceLayout.RunPlanFileName(name, attempt));
            if (!plans.Exists(root, path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Copies the CSV files of imported plans into the workspace, each once.</summary>
    private sealed class DataCopies(
        IExternalFiles files,
        Func<WorkspacePath, bool> exists,
        Func<WorkspacePath?> freeFolder,
        string root,
        string importedFile)
    {
        private readonly Dictionary<string, WorkspacePath> _copied = new(StringComparer.Ordinal);
        private WorkspacePath? _folder;

        public async Task<RunDataSource?> CopyAsync(ImportedDataFile data, string planName, List<string> warnings, CancellationToken cancellationToken)
        {
            var source = Path.IsPathRooted(data.Source)
                ? data.Source
                : Path.Combine(Path.GetDirectoryName(importedFile) ?? string.Empty, data.Source);
            var key = $"{Path.GetFullPath(source)}|{data.Header}|{data.SkipFirstLine}|{data.Delimiter}";
            if (_copied.TryGetValue(key, out var existing))
            {
                return new RunDataSource(existing, data.Mode, data.OnEof);
            }

            var text = await files.ReadTextAsync(source, cancellationToken);
            if (!text.IsSuccess)
            {
                warnings.Add($"The run plan '{planName}' has no data file: {text.Error!.Message}");
                return null;
            }

            var csv = CsvConversion.Convert(text.Value, data);
            if (csv is null)
            {
                warnings.Add($"The run plan '{planName}' has no data file: '{data.Source}' quotes values and isn't comma-separated, so it couldn't be converted.");
                return null;
            }

            _folder ??= freeFolder();
            var fileName = WorkspaceLayout.ToFolderName(Path.GetFileName(source), "data.csv");
            WorkspacePath? path = null;
            for (var attempt = 1; _folder is not null && attempt <= MaxAttempts && path is null; attempt++)
            {
                var candidate = _folder.Combine(attempt == 1 ? fileName : $"{Path.GetFileNameWithoutExtension(fileName)} {attempt}{Path.GetExtension(fileName)}");
                if (!exists(candidate) && _copied.Values.All(v => v != candidate))
                {
                    path = candidate;
                }
            }

            if (path is null)
            {
                warnings.Add($"The run plan '{planName}' has no data file: no free file name was found for '{fileName}'.");
                return null;
            }

            var saved = await files.WriteTextAsync(Path.Combine(root, path.Value), csv, cancellationToken);
            if (!saved.IsSuccess)
            {
                warnings.Add($"The run plan '{planName}' has no data file: {saved.Error!.Message}");
                return null;
            }

            _copied[key] = path;
            return new RunDataSource(path, data.Mode, data.OnEof);
        }
    }

    private sealed class Counts
    {
        /// <summary>Where each imported request was written, by the instance the importer returned.</summary>
        public Dictionary<HttpRequestDefinition, WorkspacePath> Paths { get; } = new(ReferenceEqualityComparer.Instance);

        public int Requests { get; set; }

        public int Folders { get; set; }
    }
}
