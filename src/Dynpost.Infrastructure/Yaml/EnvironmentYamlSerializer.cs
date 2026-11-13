using Dynpost.Domain.Environments;
using static Dynpost.Infrastructure.Yaml.YamlNodes;

namespace Dynpost.Infrastructure.Yaml;

/// <summary>
/// Reads and writes environment files. Keys are always written in the same order (<c>name</c>,
/// <c>cookieJar</c>, <c>variables</c>, <c>secrets</c>) and empty sections and defaults are omitted. Secret variables are listed
/// by name only: their values are never written to the file.
/// </summary>
/// <example>
/// <code>
/// name: Development
/// cookieJar: true
/// variables:
///   baseUrl: http://localhost:5000
/// secrets:
///   - apiToken
/// </code>
/// </example>
public static class EnvironmentYamlSerializer
{
    public static string Serialize(EnvironmentDefinition environment)
    {
        var writer = new YamlWriter().Scalar("name", environment.Name);
        if (environment.CookieJar)
        {
            writer.Boolean("cookieJar", true);
        }

        var variables = environment.Variables.Where(v => !v.Secret).ToList();
        if (variables.Count > 0)
        {
            writer.Mapping("variables", map =>
            {
                foreach (var variable in variables)
                {
                    map.Scalar(variable.Name, variable.Value);
                }
            });
        }

        var secrets = environment.Variables.Where(v => v.Secret).Select(v => v.Name).ToList();
        if (secrets.Count > 0)
        {
            writer.Sequence("secrets", secrets);
        }

        return writer.ToString();
    }

    /// <summary>
    /// Parses an environment file; secret variables get empty values. Throws
    /// <see cref="WorkspaceFileFormatException"/> when the text is not a valid environment.
    /// <paramref name="fallbackName"/> is used when the file has no <c>name</c>.
    /// </summary>
    public static EnvironmentDefinition Deserialize(string yaml, string fallbackName)
    {
        var root = LoadMapping(yaml);
        var name = OptionalString(root, "name");

        var variables = new List<EnvironmentVariable>();
        if (OptionalMapping(root, "variables") is { } map)
        {
            variables.AddRange(StringPairs(map, "variables").Select(pair => new EnvironmentVariable(pair.Key, pair.Value)));
        }

        foreach (var secret in StringList(root, "secrets"))
        {
            if (variables.Any(v => v.Name == secret))
            {
                throw new WorkspaceFileFormatException($"'{secret}' is listed both as a variable and as a secret.");
            }

            variables.Add(new EnvironmentVariable(secret, string.Empty, Secret: true));
        }

        var environment = new EnvironmentDefinition(string.IsNullOrWhiteSpace(name) ? fallbackName : name, variables)
        {
            CookieJar = OptionalBoolean(root, "cookieJar"),
        };
        var errors = environment.Validate(checkReferences: false);
        return errors.Count == 0 ? environment : throw new WorkspaceFileFormatException(string.Join(" ", errors));
    }
}
