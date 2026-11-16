using Dynpost.Application.About;
using Dynpost.Application.Common;
using NSubstitute;

namespace Dynpost.Application.Tests.About;

public class AboutUseCaseTests
{
    private readonly IWebBrowser _browser = Substitute.For<IWebBrowser>();

    [Fact]
    public void GetAboutInfo_IncludesTheProjectLinks()
    {
        var info = Substitute.For<IAppInfo>();
        info.Version.Returns("1.2.3");

        var about = new GetAboutInfo(info).Execute();

        Assert.Equal("1.2.3", about.Version);
        Assert.Equal("https://carlescs.github.io/dynpost/", about.Homepage);
        Assert.Equal("https://github.com/carlescs/dynpost", about.Repository);
    }

    [Theory]
    [InlineData("homepage", "https://carlescs.github.io/dynpost/")]
    [InlineData("repository", "https://github.com/carlescs/dynpost")]
    public void OpenAboutLink_OpensTheNamedPage(string link, string url)
    {
        _browser.Open(Arg.Any<Uri>()).Returns(Unit.Value);

        var result = new OpenAboutLink(_browser).Execute(link);

        Assert.True(result.IsSuccess);
        _browser.Received(1).Open(new Uri(url));
    }

    [Fact]
    public void OpenAboutLink_WithAnUnknownLink_FailsWithoutOpeningTheBrowser()
    {
        var result = new OpenAboutLink(_browser).Execute("https://example.com");

        Assert.Equal(ErrorCodes.InvalidRequest, result.Error?.Code);
        _browser.DidNotReceiveWithAnyArgs().Open(default!);
    }
}
