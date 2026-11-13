using System.ComponentModel;
using System.Diagnostics;
using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;

namespace Dynpost.Infrastructure.Workspaces;

/// <summary>
/// Opens folders with the platform's file manager: <c>explorer.exe</c> on Windows, <c>open</c>
/// (Finder) on macOS and <c>xdg-open</c> elsewhere.
/// </summary>
public sealed class SystemFileManager : IFileManager
{
    public Result<Unit> ShowFolder(string folder)
    {
        var start = new ProcessStartInfo(Command) { UseShellExecute = false };
        start.ArgumentList.Add(Path.GetFullPath(folder));
        try
        {
            // The file manager runs on its own; nothing waits for it.
            using var process = Process.Start(start);
            return process is null
                ? new Error(ErrorCodes.Io, "The file manager could not be started.")
                : Unit.Value;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new Error(ErrorCodes.Io, $"The file manager could not be started: {ex.Message}");
        }
    }

    private static string Command =>
        OperatingSystem.IsWindows() ? "explorer.exe"
        : OperatingSystem.IsMacOS() ? "open"
        : "xdg-open";
}
