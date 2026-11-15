using Dynpost.Domain.Http;

namespace Dynpost.Domain.Tests.Http;

public class ClientCertificateSettingTests
{
    [Theory]
    [InlineData("", "https://anything.test/", true)]
    [InlineData("api.test", "https://API.test/x", true)]
    [InlineData("api.test", "https://other.test/", false)]
    [InlineData("*.example.com", "https://a.b.example.com/", true)]
    [InlineData("*.example.com", "https://example.com/", false)]
    [InlineData("api.test:8443", "https://api.test:8443/", true)]
    [InlineData("api.test:8443", "https://api.test/", false)]
    public void Matches_HostsWildcardsAndPorts(string host, string url, bool expected) =>
        Assert.Equal(expected, new ClientCertificateSetting(host, "c.pfx").Matches(new Uri(url)));

    [Fact]
    public void For_TakesTheFirstMatchWithACertificate()
    {
        var settings = new[]
        {
            new ClientCertificateSetting("api.test", ""),
            new ClientCertificateSetting("api.test", "specific.pfx"),
            new ClientCertificateSetting("", "fallback.pfx"),
        };

        Assert.Equal("specific.pfx", ClientCertificateSetting.For(settings, new Uri("https://api.test/"))!.Certificate);
        Assert.Equal("fallback.pfx", ClientCertificateSetting.For(settings, new Uri("https://other.test/"))!.Certificate);
    }
}
