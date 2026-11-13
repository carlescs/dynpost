using Dynpost.Domain.Requests;

namespace Dynpost.Domain.Environments;

/// <summary>
/// A variable of an environment. The value of a secret variable is kept on this computer only;
/// the environment file lists just its name.
/// </summary>
public sealed record EnvironmentVariable(string Name, string Value, bool Secret = false);

/// <summary>A named set of variables: the content of one file in the <c>environments</c> folder.</summary>
public sealed record EnvironmentDefinition
{
    public EnvironmentDefinition(string name, IReadOnlyList<EnvironmentVariable>? variables = null)
    {
        Name = name;
        Variables = variables ?? [];
    }

    public string Name { get; init; }

    public IReadOnlyList<EnvironmentVariable> Variables { get; init; }

    /// <summary>
    /// Whether requests sent with this environment keep cookies between them. The cookies stay on
    /// this computer; only this switch is part of the file. Off by default.
    /// </summary>
    public bool CookieJar { get; init; }

    /// <summary>
    /// Checks the rules an environment must satisfy before it can be saved. Returns an empty list
    /// when the environment is valid.
    /// </summary>
    /// <param name="checkReferences">
    /// Whether variables referring to each other in a loop are an error. Files are read without
    /// this check, so such an environment can still be opened and fixed.
    /// </param>
    public IReadOnlyList<string> Validate(bool checkReferences = true)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("The environment needs a name.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
            {
                errors.Add("Every variable needs a name.");
            }
            else if (!VariableTemplate.IsValidName(variable.Name))
            {
                errors.Add($"'{variable.Name}' is not a valid variable name: it can't contain spaces or braces.");
            }
            else if (!seen.Add(variable.Name))
            {
                errors.Add($"The variable '{variable.Name}' is defined more than once.");
            }
        }

        var cycle = checkReferences ? VariableSet.Resolve(Variables).Cycle : [];
        if (cycle.Count > 0)
        {
            errors.Add(VariableSet.CycleMessage(cycle));
        }

        return errors;
    }
}
