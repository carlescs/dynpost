using Dynpost.Application.Requests;
using Dynpost.Domain.Environments;
using Dynpost.Domain.Requests;

namespace Dynpost.Application.Interchange;

/// <summary>
/// Adapts requests and environments read from other apps to the rules Dynpost files follow, so
/// they can be saved: names are filled in, and entries Dynpost can't represent (unnamed, repeated)
/// are dropped with a warning.
/// </summary>
internal static class ImportSanitizer
{
    public const string UntitledRequest = "Untitled request";
    public const string UntitledEnvironment = "Imported environment";
    private const int MaxNameLength = 200;

    /// <summary>The request ready to save, or <c>null</c> (with a warning) when it can't be.</summary>
    public static HttpRequestDefinition? Request(HttpRequestDefinition request, List<string> warnings)
    {
        var name = OneLine(request.Name);
        if (name.Length == 0)
        {
            name = OneLine($"{request.Method.ToName()} {request.Url}");
        }

        if (name.Length == 0)
        {
            name = UntitledRequest;
        }

        var cleaned = request with
        {
            Name = name.Length > MaxNameLength ? name[..MaxNameLength].TrimEnd() : name,
            PathParams = PathParameters.Sync(request.Url, request.PathParams),
            Params = Entries(request.Params, "query parameter", StringComparer.Ordinal, $"'{name}'", warnings),
            Headers = Entries(request.Headers, "header", StringComparer.OrdinalIgnoreCase, $"'{name}'", warnings),
            Description = RequestMapper.NormalizeDescription(request.Description),
        };

        var errors = cleaned.Validate();
        if (errors.Count > 0)
        {
            warnings.Add($"Skipped the request '{name}': {string.Join(" ", errors)}");
            return null;
        }

        return cleaned;
    }

    /// <summary>A run plan's name on one line, at most as long as a request's.</summary>
    public static string PlanName(string name)
    {
        var line = OneLine(name);
        line = line.Length > MaxNameLength ? line[..MaxNameLength].TrimEnd() : line;
        return line.Length == 0 ? "Imported plan" : line;
    }

    public static EnvironmentDefinition Environment(EnvironmentDefinition environment, List<string> warnings)
    {
        var name = OneLine(environment.Name);
        if (name.Length == 0)
        {
            name = UntitledEnvironment;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var variables = new List<EnvironmentVariable>();
        foreach (var variable in environment.Variables)
        {
            var variableName = variable.Name.Trim();
            if (!VariableTemplate.IsValidName(variableName))
            {
                warnings.Add($"Skipped the variable '{variable.Name}' of the environment '{name}': names can't be empty or contain spaces or braces.");
            }
            else if (!seen.Add(variableName))
            {
                warnings.Add($"Skipped a repeated variable '{variableName}' in the environment '{name}'.");
            }
            else
            {
                variables.Add(variable with { Name = variableName });
            }
        }

        // Stored order: plain variables first, then secrets (as the editor saves them).
        return new EnvironmentDefinition(name, variables.OrderBy(v => v.Secret).ToList());
    }

    /// <summary>
    /// The settings of an imported folder ready to save: repeated headers and variables and
    /// invalid variable names are dropped; <c>null</c> (with a warning) when they still can't be saved.
    /// </summary>
    public static FolderDefaults? Folder(FolderDefaults defaults, string folderName, List<string> warnings)
    {
        var owner = $"the folder '{OneLine(folderName)}'";
        var variables = new List<KeyValueEntry>();
        foreach (var variable in Entries(defaults.Variables, "variable", StringComparer.Ordinal, owner, warnings))
        {
            if (VariableTemplate.IsValidName(variable.Key))
            {
                variables.Add(variable);
            }
            else
            {
                warnings.Add($"Skipped the variable '{variable.Key}' of {owner}: names can't contain spaces or braces.");
            }
        }

        var cleaned = defaults with
        {
            Headers = Entries(defaults.Headers, "header", StringComparer.OrdinalIgnoreCase, owner, warnings),
            Variables = variables,
        };

        var errors = cleaned.Validate();
        if (errors.Count > 0)
        {
            warnings.Add($"Skipped the settings of {owner}: {string.Join(" ", errors)}");
            return null;
        }

        return cleaned.IsEmpty ? null : cleaned;
    }

    private static List<KeyValueEntry> Entries(
        IReadOnlyList<KeyValueEntry> entries,
        string kind,
        StringComparer comparer,
        string owner,
        List<string> warnings)
    {
        var seen = new HashSet<string>(comparer);
        var result = new List<KeyValueEntry>();
        foreach (var entry in entries)
        {
            var key = entry.Key.Trim().TrimStart(HttpRequestDefinition.DisabledPrefix[0]).Trim();
            if (key.Length == 0)
            {
                if (entry.Value.Length > 0)
                {
                    warnings.Add($"Skipped a {kind} without a name in {owner}.");
                }
            }
            else if (!seen.Add(key))
            {
                warnings.Add($"Skipped the repeated {kind} '{key}' in {owner}; Dynpost keeps one value per name.");
            }
            else
            {
                result.Add(entry with { Key = key });
            }
        }

        return result;
    }

    private static string OneLine(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
}
