using Missive.Application.History;

namespace Missive.Application.Settings;

/// <summary>
/// Per-user application settings, kept outside any workspace (they are machine-specific and must
/// never end up in a repository).
/// </summary>
/// <param name="LastWorkspacePath">The workspace folder to reopen at startup.</param>
/// <param name="Theme">The color theme, <see cref="AppThemes.Light"/> or <see cref="AppThemes.Dark"/>; <c>null</c> is light.</param>
/// <param name="RecentWorkspaces">Workspace folders opened recently, the most recent first.</param>
/// <param name="History">How responses are kept in the requests' history; <c>null</c> is <see cref="HistorySettings.Default"/>.</param>
public sealed record AppSettings(
    string? LastWorkspacePath = null,
    string? Theme = null,
    IReadOnlyList<string>? RecentWorkspaces = null,
    HistorySettings? History = null)
{
    public static AppSettings Default { get; } = new();
}

/// <summary>The color themes the UI supports.</summary>
public static class AppThemes
{
    public const string Light = "light";
    public const string Dark = "dark";

    public static bool IsValid(string? theme) => theme is Light or Dark;
}
