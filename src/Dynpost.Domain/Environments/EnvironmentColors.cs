namespace Dynpost.Domain.Environments;

/// <summary>
/// The colors an environment can be marked with, so it is recognized at a glance (e.g. red for
/// production). Stored by name in the environment file; the app decides how each one looks.
/// </summary>
public static class EnvironmentColors
{
    /// <summary>Every color, in the order the app offers them.</summary>
    public static IReadOnlyList<string> All { get; } =
        ["red", "orange", "yellow", "green", "teal", "blue", "purple", "pink", "gray"];

    public static bool IsValid(string? color) => color is not null && All.Contains(color, StringComparer.Ordinal);
}
