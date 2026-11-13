using Dynypost.Application.Common;
using Dynypost.Domain.Environments;
using Dynypost.Domain.Workspaces;

namespace Dynypost.Application.Environments;

/// <summary>Converts between environment DTOs and the domain model, and checks environment ids.</summary>
public static class EnvironmentMapper
{
    /// <summary>
    /// Builds a valid domain environment from a DTO. Rows with neither name nor value (the blank
    /// row the editor keeps at the end of the table) are dropped. Variables are put in the order
    /// they are stored in: plain variables first, then secrets.
    /// </summary>
    public static Result<EnvironmentDefinition> ToDomain(EnvironmentDto dto)
    {
        var variables = (dto.Variables ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v.Name) || !string.IsNullOrEmpty(v.Value))
            .Select(v => new EnvironmentVariable((v.Name ?? string.Empty).Trim(), v.Value ?? string.Empty, v.Secret))
            .OrderBy(v => v.Secret)
            .ToList();

        var environment = new EnvironmentDefinition((dto.Name ?? string.Empty).Trim(), variables) { CookieJar = dto.CookieJar };
        var errors = environment.Validate();
        return errors.Count == 0
            ? environment
            : new Error(ErrorCodes.InvalidRequest, string.Join(" ", errors));
    }

    /// <summary>The DTO for an environment, with the stored values of its secrets filled in.</summary>
    public static EnvironmentDto ToDto(EnvironmentDefinition environment, IReadOnlyDictionary<string, string>? secrets) =>
        new(environment.Name, environment.Variables
            .Select(v => v.Secret
                ? new EnvironmentVariableDto(v.Name, secrets?.GetValueOrDefault(v.Name) ?? string.Empty, true)
                : new EnvironmentVariableDto(v.Name, v.Value))
            .ToList(),
            environment.CookieJar);

    /// <summary>The values of an environment's secret variables, without the empty ones.</summary>
    public static IReadOnlyDictionary<string, string> SecretValues(EnvironmentDefinition environment) =>
        environment.Variables
            .Where(v => v.Secret && v.Value.Length > 0)
            .ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="id"/> names a file directly inside the <c>environments</c> folder.</summary>
    public static bool IsValidId(string? id) =>
        id is not null
        && WorkspacePath.TryParse(id, out var path)
        && path.Segments.Count == 1
        && path.Value == id
        && WorkspaceLayout.IsEnvironmentFile(id);

    internal static Error InvalidId(string? id) =>
        new(ErrorCodes.InvalidPath, $"'{id}' is not a valid environment file name.");

    internal static Error NotFound(string id) =>
        new(ErrorCodes.NotFound, $"The environment '{id}' does not exist.");
}
