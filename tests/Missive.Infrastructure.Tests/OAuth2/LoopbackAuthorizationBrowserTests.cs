using System.Net;
using System.Net.Sockets;
using Missive.Application.OAuth2;
using Missive.Infrastructure.OAuth2;

namespace Missive.Infrastructure.Tests.OAuth2;

public class LoopbackAuthorizationBrowserTests
{
    /// <summary>Plays the browser: follows the redirect the authorization server would send.</summary>
    private static Action<Uri> Browser(Func<Uri, string> redirectTarget, List<HttpStatusCode>? statuses = null) => url =>
    {
        var redirect = new Uri(Uri.UnescapeDataString(url.Query.Split("redirect_uri=")[1].Split('&')[0]));
        _ = Task.Run(async () =>
        {
            using var client = new HttpClient();
            var favicon = await client.GetAsync(new Uri(redirect, "/favicon.ico"));
            statuses?.Add(favicon.StatusCode);
            var answer = await client.GetAsync(new Uri(redirect, redirectTarget(redirect)));
            statuses?.Add(answer.StatusCode);
        });
    };

    private static Uri AuthorizationUrl(Uri redirect) =>
        new($"https://auth.test/authorize?redirect_uri={Uri.EscapeDataString(redirect.AbsoluteUri)}&state=s");

    [Fact]
    public async Task Authorize_ReceivesTheCodeOnAFreeLoopbackPort()
    {
        var statuses = new List<HttpStatusCode>();
        var browser = new LoopbackAuthorizationBrowser(Browser(r => r.AbsolutePath + "?code=a%2Fb&state=s", statuses));

        var callback = await browser.AuthorizeAsync(null, AuthorizationUrl);

        Assert.True(callback.IsSuccess, callback.Error?.Message);
        Assert.Equal("127.0.0.1", callback.Value.RedirectUri.Host);
        Assert.Equal("/callback", callback.Value.RedirectUri.AbsolutePath);
        Assert.Equal("a/b", callback.Value.Parameters["code"]);
        Assert.Equal("s", callback.Value.Parameters["state"]);
        await WaitUntil(() => statuses.Count == 2);
        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.OK], statuses);
    }

    [Fact]
    public async Task Authorize_ListensOnAFixedRedirect()
    {
        var port = FreePort();
        var redirect = new Uri($"http://localhost:{port}/oauth/done");
        var browser = new LoopbackAuthorizationBrowser(Browser(r => "/oauth/done?error=access_denied"));

        var callback = await browser.AuthorizeAsync(redirect, AuthorizationUrl);

        Assert.Equal(redirect, callback.Value.RedirectUri);
        Assert.Equal("access_denied", callback.Value.Parameters["error"]);
    }

    [Fact]
    public async Task Authorize_TimesOut()
    {
        var browser = new LoopbackAuthorizationBrowser(_ => { }, TimeSpan.FromMilliseconds(200));

        var callback = await browser.AuthorizeAsync(null, AuthorizationUrl);

        Assert.Equal(OAuth2Errors.FailedCode, callback.Error?.Code);
        Assert.Contains("No answer", callback.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelPending_StopsWaiting()
    {
        var browser = new LoopbackAuthorizationBrowser(_ => { });
        Assert.False(browser.CancelPending());

        var waiting = browser.AuthorizeAsync(null, AuthorizationUrl);
        var cancelled = false;
        await WaitUntil(() => cancelled = cancelled || browser.CancelPending());
        var callback = await waiting;

        Assert.Equal("The sign-in was cancelled.", callback.Error?.Message);
    }

    [Fact]
    public async Task Authorize_PortInUse_IsAnError()
    {
        var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        try
        {
            var port = ((IPEndPoint)taken.LocalEndpoint).Port;
            var browser = new LoopbackAuthorizationBrowser(_ => throw new InvalidOperationException("must not open"));

            var callback = await browser.AuthorizeAsync(new Uri($"http://127.0.0.1:{port}/cb"), AuthorizationUrl);

            Assert.Contains($"port {port}", callback.Error?.Message, StringComparison.Ordinal);
        }
        finally
        {
            taken.Stop();
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition());
    }
}
