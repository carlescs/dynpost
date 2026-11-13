using System.Text.RegularExpressions;
using Dynypost.Application.Common;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Workspaces;
using Dynypost.Infrastructure.Git;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using static Dynypost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynypost.Infrastructure.Workspaces;

/// <summary>
/// Keeps a workspace's layout version as <c>layout</c> in its <c>dynypost.yaml</c>, and finds the
/// files of the old layout, where requests and run plans could be in any folder.
/// </summary>
public sealed partial class FileSystemWorkspaceLayoutStore : IWorkspaceLayoutStore
{
    private const string LayoutKey = "layout";

    /// <summary>The top-level keys of a request file; a YAML file with only these keys is a request.</summary>
    private static readonly HashSet<string> RequestKeys = new(StringComparer.Ordinal)
    {
        "name", "method", "url", "params", "headers", "auth", "body", "capture", "description",
    };

    private static WorkspacePath WorkspaceFile { get; } = WorkspacePath.Root.Combine(WorkspaceLayout.WorkspaceFile);

    public async Task<Result<int>> ReadVersionAsync(string root, CancellationToken cancellationToken = default)
    {
        string? text;
        try
        {
            text = await ReadIfExistsAsync(FullPath(root, WorkspaceFile), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read '{WorkspaceLayout.WorkspaceFile}': {ex.Message}");
        }

        var match = text is null ? null : LayoutLine().Match(text);
        return match is { Success: true } && int.TryParse(match.Groups["version"].Value, out var version) ? version : 1;
    }

    public async Task<Result<Unit>> WriteVersionAsync(string root, int version, CancellationToken cancellationToken = default)
    {
        var line = $"{LayoutKey}: {version}";
        try
        {
            var fullPath = FullPath(root, WorkspaceFile);
            var text = (await ReadIfExistsAsync(fullPath, cancellationToken)) ?? string.Empty;
            var updated = LayoutLine().IsMatch(text)
                ? LayoutLine().Replace(text, line, 1)
                : text + (text.Length == 0 || text.EndsWith('\n') ? string.Empty : "\n") + line + "\n";
            await WriteIfChangedAsync(fullPath, updated, cancellationToken);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not write '{WorkspaceLayout.WorkspaceFile}': {ex.Message}");
        }
    }

    public async Task<Result<Unit>> RenameLegacyFilesAsync(string root, CancellationToken cancellationToken = default)
    {
        try
        {
            var legacyFile = Path.Combine(root, WorkspaceLayout.LegacyWorkspaceFile);
            var file = Path.Combine(root, WorkspaceLayout.WorkspaceFile);
            if (File.Exists(legacyFile) && !File.Exists(file) && !Directory.Exists(file))
            {
                File.Move(legacyFile, file);
            }

            var legacyFolder = Path.Combine(root, WorkspaceLayout.LegacyLocalFolder);
            var folder = Path.Combine(root, WorkspaceLayout.LocalFolder);
            if (Directory.Exists(legacyFolder) && !Directory.Exists(folder) && !File.Exists(folder))
            {
                Directory.Move(legacyFolder, folder);
            }

            await GitIgnoreFile.RenameLegacyFolderAsync(root, cancellationToken);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not rename the files of the app's former name: {ex.Message}");
        }
    }

    public async Task<Result<LegacyLayoutScan>> ScanLegacyAsync(string root, CancellationToken cancellationToken = default)
    {
        var requests = new List<WorkspacePath>();
        var plans = new List<WorkspacePath>();
        var empty = new List<WorkspacePath>();
        try
        {
            await ScanAsync(WorkspacePath.Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not look through the workspace folder: {ex.Message}");
        }

        return new LegacyLayoutScan(requests, plans, empty);

        // Returns whether the folder holds any file other than OS clutter, at any depth.
        async Task<bool> ScanAsync(WorkspacePath folder)
        {
            var holdsFiles = false;
            var directory = new DirectoryInfo(FullPath(root, folder));
            foreach (var info in directory.EnumerateFileSystemInfos().OrderBy(i => i.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!WorkspacePath.TryParse(info.Name, out var parsed) || parsed.Value != info.Name)
                {
                    holdsFiles = true;
                    continue;
                }

                if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    holdsFiles = true;
                    continue;
                }

                var path = folder.Combine(info.Name);
                if (info is DirectoryInfo)
                {
                    if (info.Name.StartsWith('.')
                        || (folder.IsRoot && string.Equals(info.Name, WorkspaceLayout.EnvironmentsFolder, StringComparison.OrdinalIgnoreCase)))
                    {
                        holdsFiles = true;
                        continue;
                    }

                    var childHoldsFiles = await ScanAsync(path);
                    holdsFiles |= childHoldsFiles;
                    if (!childHoldsFiles && !IsInArea(path))
                    {
                        empty.Add(path);
                    }

                    continue;
                }

                if (!WorkspaceLayout.IsClutterFile(info.Name))
                {
                    holdsFiles = true;
                }

                if (WorkspaceLayout.IsRunPlanFileName(info.Name))
                {
                    if (!WorkspaceLayout.IsRunPlanPath(path))
                    {
                        plans.Add(path);
                    }
                }
                else if (string.Equals(info.Name, WorkspaceLayout.FolderDefaultsFile, StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsInArea(path.Parent, WorkspaceLayout.RequestsRoot))
                    {
                        requests.Add(path);
                    }
                }
                else if (WorkspaceLayout.IsRequestFile(folder, info.Name)
                    && !WorkspaceLayout.IsRequestPath(path)
                    && await LooksLikeRequestAsync(FullPath(root, path), cancellationToken))
                {
                    requests.Add(path);
                }
            }

            return holdsFiles;
        }
    }

    private static bool IsInArea(WorkspacePath path) =>
        IsInArea(path, WorkspaceLayout.RequestsRoot) || IsInArea(path, WorkspaceLayout.PlansRoot);

    private static bool IsInArea(WorkspacePath path, WorkspacePath area) =>
        path == area || path.Value.StartsWith(area.Value + "/", StringComparison.Ordinal);

    /// <summary>
    /// Whether a YAML file is a Dynypost request: a mapping with a <c>method</c>, or with only the
    /// keys requests have. Other YAML files (CI or compose files, OpenAPI documents) are left alone.
    /// </summary>
    private static async Task<bool> LooksLikeRequestAsync(string fullPath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(fullPath, Utf8NoBom, cancellationToken);
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode { Children.Count: > 0 } mapping)
            {
                return false;
            }

            var keys = mapping.Children.Keys.OfType<YamlScalarNode>().Select(k => k.Value ?? string.Empty).ToList();
            return keys.Count == mapping.Children.Count && (keys.Contains("method") || keys.All(RequestKeys.Contains));
        }
        catch (YamlException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^layout:[ \t]*(?<version>\d+)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex LayoutLine();
}
