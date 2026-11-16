using System.ComponentModel;
using System.Diagnostics;
using Dynpost.Application.About;
using Dynpost.Application.Common;

namespace Dynpost.Infrastructure.About;

/// <summary>
/// Opens web pages with the user's default browser: through the shell on Windows, <c>open</c> on
/// macOS and <c>xdg-open</c> elsewhere.
/// </summary>
public sealed class SystemWebBrowser : IWebBrowser
{
    public Result<Unit> Open(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
        {
            return new Error(ErrorCodes.InvalidRequest, $"Only web pages can be opened: {url}");
        }

        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }
            : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { ArgumentList = { url.AbsoluteUri } };
        try
        {
            // The browser runs on its own; nothing waits for it.
            using var process = Process.Start(start);
            return Unit.Value;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new Error(ErrorCodes.Io, $"The browser could not be started: {ex.Message}");
        }
    }
}
