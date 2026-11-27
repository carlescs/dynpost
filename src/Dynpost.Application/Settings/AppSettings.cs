using Dynpost.Application.History;
using Dynpost.Application.Mcp;
using Dynpost.Application.Secrets;

namespace Dynpost.Application.Settings;

/// <summary>
/// Per-user application settings, kept outside any workspace (they are machine-specific and must
/// never end up in a repository).
/// </summary>
/// <param name="LastWorkspacePath">The workspace folder to reopen at startup.</param>
/// <param name="Theme">The color theme, <see cref="AppThemes.Light"/> or <see cref="AppThemes.Dark"/>; <c>null</c> is light.</param>
/// <param name="RecentWorkspaces">Workspace folders opened recently, the most recent first.</param>
/// <param name="History">How responses are kept in the requests' history; <c>null</c> is <see cref="HistorySettings.Default"/>.</param>
/// <param name="ActivityOrder">
/// The order of the activity bar's views (their ids, such as <c>explorer</c> or <c>git</c>);
/// <c>null</c> is the UI's default order. The UI places views missing from it at the end.
/// </param>
/// <param name="Mcp">The MCP server AI assistants connect to; <c>null</c> is <see cref="McpSettings.Default"/> (off).</param>
/// <param name="Secrets">Where secret values are kept; <c>null</c> is <see cref="SecretSettings.Default"/>.</param>
/// <param name="NativeTitleBar">
/// Whether the window keeps the operating system's title bar instead of the app's own; <c>null</c>
/// is <c>false</c>. Read when the window opens, so a change applies the next time the app starts.
/// </param>
/// <param name="Tray">Whether the window hides to the system tray; <c>null</c> is <see cref="TraySettings.Default"/> (never).</param>
public sealed record AppSettings(
    string? LastWorkspacePath = null,
    string? Theme = null,
    IReadOnlyList<string>? RecentWorkspaces = null,
    HistorySettings? History = null,
    IReadOnlyList<string>? ActivityOrder = null,
    McpSettings? Mcp = null,
    SecretSettings? Secrets = null,
    bool? NativeTitleBar = null,
    TraySettings? Tray = null)
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
