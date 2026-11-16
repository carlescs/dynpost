using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Domain.Environments;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Yaml;
using static Dynpost.Infrastructure.Workspaces.WorkspaceFiles;
using static Dynpost.Infrastructure.Yaml.YamlNodes;

namespace Dynpost.Infrastructure.Workspaces;

/// <summary>
/// Keeps the workspace-global variables in <c>dynpost.yaml</c>, as <c>variables</c> (names and
/// values) and <c>secrets</c> (names only), written like an environment's after the file's other
/// keys, which are kept as written.
/// </summary>
/// <example>
/// <code>
/// layout: 2
/// variables:
///   baseUrl: https://api.example.com
/// secrets:
///   - apiToken
/// </code>
/// </example>
public sealed class YamlWorkspaceVariablesStore : IWorkspaceVariablesStore
{
    private static WorkspacePath WorkspaceFile { get; } = WorkspacePath.Root.Combine(WorkspaceLayout.WorkspaceFile);

    public async Task<Result<IReadOnlyList<EnvironmentVariable>>> ReadAsync(string root, CancellationToken cancellationToken = default)
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

        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<EnvironmentVariable>();
        }

        try
        {
            var map = LoadMapping(text);
            var variables = new List<EnvironmentVariable>();
            if (OptionalMapping(map, "variables") is { } pairs)
            {
                variables.AddRange(StringPairs(pairs, "variables").Select(pair => new EnvironmentVariable(pair.Key, pair.Value)));
            }

            foreach (var secret in StringList(map, "secrets"))
            {
                if (variables.Any(v => v.Name == secret))
                {
                    throw new WorkspaceFileFormatException($"'{secret}' is listed both as a variable and as a secret.");
                }

                variables.Add(new EnvironmentVariable(secret, string.Empty, Secret: true));
            }

            return variables;
        }
        catch (WorkspaceFileFormatException ex)
        {
            return new Error(ErrorCodes.InvalidFile, $"'{WorkspaceLayout.WorkspaceFile}' has invalid global variables. {ex.Message}");
        }
    }

    public async Task<Result<bool>> WriteAsync(string root, IReadOnlyList<EnvironmentVariable> variables, CancellationToken cancellationToken = default)
    {
        var path = FullPath(root, WorkspaceFile);
        try
        {
            var text = (await ReadIfExistsAsync(path, cancellationToken) ?? string.Empty).ReplaceLineEndings("\n");
            var kept = WithoutGlobals(text);

            var writer = new YamlWriter();
            var plain = variables.Where(v => !v.Secret).ToList();
            if (plain.Count > 0)
            {
                writer.Mapping("variables", map =>
                {
                    foreach (var variable in plain)
                    {
                        map.Scalar(variable.Name, variable.Value);
                    }
                });
            }

            var secrets = variables.Where(v => v.Secret).Select(v => v.Name).ToList();
            if (secrets.Count > 0)
            {
                writer.Sequence("secrets", secrets);
            }

            var content = kept + writer;
            if (content.Length == 0 && !File.Exists(path))
            {
                return false;
            }

            return await WriteIfChangedAsync(path, content, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not write '{WorkspaceLayout.WorkspaceFile}': {ex.Message}");
        }
    }

    /// <summary>
    /// The file without its <c>variables</c> and <c>secrets</c> keys: a top-level key line and the
    /// indented, list and blank lines under it. Everything else is kept as written.
    /// </summary>
    private static string WithoutGlobals(string text)
    {
        var lines = text.Split('\n');
        var count = text.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        var kept = new List<string>();
        var skipping = false;
        for (var i = 0; i < count; i++)
        {
            var line = lines[i];
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && line[0] is not ('-' or '#'))
            {
                var colon = line.IndexOf(':');
                var key = (colon < 0 ? line : line[..colon]).Trim().Trim('"', '\'');
                skipping = key is "variables" or "secrets";
            }

            if (!skipping)
            {
                kept.Add(line);
            }
        }

        while (kept.Count > 0 && kept[^1].Trim().Length == 0)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return kept.Count == 0 ? string.Empty : string.Join('\n', kept) + "\n";
    }
}
