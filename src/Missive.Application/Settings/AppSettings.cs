namespace Missive.Application.Settings;

/// <summary>
/// Per-user application settings, kept outside any workspace (they are machine-specific and must
/// never end up in a repository).
/// </summary>
/// <param name="LastWorkspacePath">The workspace folder to reopen at startup.</param>
public sealed record AppSettings(string? LastWorkspacePath = null)
{
    public static AppSettings Default { get; } = new();
}
