using System.Runtime.InteropServices;
using Photino.NET;

namespace Missive.Desktop.Hosting;

/// <summary>
/// Serves the bundled Angular frontend from <c>wwwroot</c> through a Photino custom
/// scheme handler, so the UI is never loaded from <c>file://</c>.
/// </summary>
/// <remarks>
/// Chromium-based webviews (WebView2 on Windows) refuse to run the module scripts the
/// Angular build emits when the page has a <c>file://</c> origin. Serving assets from
/// a custom scheme gives the page a real origin on every platform.
/// WebView2 cannot navigate to arbitrary custom schemes through Photino, so on Windows
/// the handler intercepts <c>http://localhost/</c> instead (the same approach
/// Photino.Blazor uses); other platforms use <c>app://localhost/</c>.
/// </remarks>
internal sealed class BundledAssets
{
    private const string Host = "localhost";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html",
        [".js"] = "text/javascript",
        [".mjs"] = "text/javascript",
        [".css"] = "text/css",
        [".json"] = "application/json",
        [".map"] = "application/json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".txt"] = "text/plain",
    };

    private readonly string _root;

    public BundledAssets(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public static string Scheme { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "http" : "app";

    public static Uri StartUri { get; } = new($"{Scheme}://{Host}/index.html");

    public Stream? Handle(object sender, string scheme, string url, out string contentType)
    {
        contentType = "text/plain";

        var path = ResolvePath(url);
        if (path is null)
        {
            return null;
        }

        contentType = ContentTypes.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        return File.OpenRead(path);
    }

    /// <summary>
    /// Maps a request URL to a file inside the asset root, or <c>null</c> when the URL is
    /// for another host, escapes the root, or names a missing file.
    /// </summary>
    internal string? ResolvePath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (relative.Length == 0)
        {
            relative = "index.html";
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, relative));
        var rootWithSeparator = Path.EndsInDirectorySeparator(_root) ? _root : _root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal) || !File.Exists(fullPath))
        {
            return null;
        }

        return fullPath;
    }
}
