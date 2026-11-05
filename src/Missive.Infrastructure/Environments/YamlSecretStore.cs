using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Domain.Workspaces;
using Missive.Infrastructure.Yaml;
using YamlDotNet.RepresentationModel;
using static Missive.Infrastructure.Workspaces.WorkspaceFiles;
using static Missive.Infrastructure.Yaml.YamlNodes;

namespace Missive.Infrastructure.Environments;

/// <summary>
/// Keeps secret variable values in <c>.missive/secrets.yaml</c>, which is git-ignored: one
/// mapping of variable names to values per environment file name.
/// </summary>
/// <example>
/// <code>
/// dev.yaml:
///   apiToken: s3cr3t
/// </code>
/// </example>
public sealed class YamlSecretStore : ISecretStore
{
    public const string FileName = "secrets.yaml";

    private const string Header = "# Secret variable values for this computer. Missive keeps this file out of git.\n";

    public async Task<Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>> LoadAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        string? text;
        try
        {
            text = await ReadIfExistsAsync(LocalWorkspaceFolder.FilePath(root, FileName), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read the secrets file: {ex.Message}");
        }

        var secrets = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (text is null)
        {
            return secrets;
        }

        try
        {
            var map = LoadMapping(text);
            foreach (var (key, value) in map.Children)
            {
                var id = ScalarText(key, "environment");
                if (IsNull(value))
                {
                    continue;
                }

                if (value is not YamlMappingNode values)
                {
                    throw new WorkspaceFileFormatException($"'{id}' must be a mapping of names to values.");
                }

                secrets[id] = StringPairs(values, id).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }
        }
        catch (WorkspaceFileFormatException ex)
        {
            return new Error(
                ErrorCodes.InvalidFile,
                $"'{WorkspaceLayout.LocalFolder}/{FileName}' is not a valid secrets file. {ex.Message}");
        }

        return secrets;
    }

    public async Task<Result<Unit>> SaveAsync(
        string root,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> secrets,
        CancellationToken cancellationToken = default)
    {
        var environments = secrets
            .Where(entry => entry.Value.Count > 0)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        try
        {
            if (environments.Count == 0)
            {
                LocalWorkspaceFolder.Delete(root, FileName);
                return Unit.Value;
            }

            var writer = new YamlWriter();
            foreach (var (id, values) in environments)
            {
                writer.Mapping(id, map =>
                {
                    foreach (var (name, value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        map.Scalar(name, value);
                    }
                });
            }

            await LocalWorkspaceFolder.WriteAsync(root, FileName, Header + writer, cancellationToken);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not save the secrets file: {ex.Message}");
        }
    }
}
