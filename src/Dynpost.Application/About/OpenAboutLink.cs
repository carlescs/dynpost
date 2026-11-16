using Dynpost.Application.Common;

namespace Dynpost.Application.About;

/// <summary>Opens a web page in the user's browser.</summary>
public interface IWebBrowser
{
    Result<Unit> Open(Uri url);
}

/// <summary>
/// Opens one of the About page's links in the system browser. The page names the link rather than
/// passing a URL, so the webview can't open arbitrary addresses.
/// </summary>
public sealed class OpenAboutLink(IWebBrowser browser)
{
    public Result<Unit> Execute(string link) => link switch
    {
        AboutLinks.HomepageLink => browser.Open(new Uri(AboutLinks.Homepage)),
        AboutLinks.RepositoryLink => browser.Open(new Uri(AboutLinks.Repository)),
        _ => new Error(ErrorCodes.InvalidRequest, $"Unknown link '{link}'."),
    };
}
