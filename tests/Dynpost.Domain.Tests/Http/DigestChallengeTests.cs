using Dynpost.Domain.Http;

namespace Dynpost.Domain.Tests.Http;

public class DigestChallengeTests
{
    [Fact]
    public void Authorization_MatchesTheRfc2617Example()
    {
        var challenge = DigestChallenge.Parse(
        [
            """Digest realm="testrealm@host.com", qop="auth,auth-int", nonce="dcd98b7102dd2f0e8b11d0f600bfb0c093", opaque="5ccc069c403ebaf9f0171e9517f40e41" """,
        ])!;

        var header = challenge.Authorization(new DigestCredentials("Mufasa", "Circle Of Life"), "GET", "/dir/index.html", "0a4f113b");

        Assert.Contains("response=\"6629fae49393a05397450978507c4ef1\"", header);
        Assert.Contains("qop=auth, nc=00000001, cnonce=\"0a4f113b\"", header);
        Assert.Contains("opaque=\"5ccc069c403ebaf9f0171e9517f40e41\"", header);
        Assert.StartsWith("Digest username=\"Mufasa\"", header);
    }

    [Fact]
    public void Parse_PrefersSha256AndSkipsOtherSchemes()
    {
        var challenge = DigestChallenge.Parse(
        [
            "Basic realm=\"x\", Digest realm=\"http-auth@example.org\", qop=\"auth\", algorithm=MD5, nonce=\"n1\"",
            "Digest realm=\"http-auth@example.org\", qop=\"auth\", algorithm=SHA-256, nonce=\"7ypf/xlj9XXwfDPEoM4URrv/xwf94BcCAzFZH4GiTo0v\", opaque=\"FQhe/qaU925kfnzjCev0ciny7QMkPqMAFRtzCUYo5tdS\"",
        ])!;

        Assert.Equal("SHA-256", challenge.Algorithm);
        var header = challenge.Authorization(
            new DigestCredentials("Mufasa", "Circle of Life"),
            "GET",
            "/dir/index.html",
            "f2/wE4q74E6zIJEtWaHKaf5wv/H5QzzpXusqGemxURZJ");
        Assert.Contains("response=\"753927fa0e85d155564e2e272a28d1802ca10daf4496794697cf8db5856cb6c1\"", header);
    }

    [Fact]
    public void Parse_IgnoresUnsupportedAlgorithmsAndNonDigestChallenges()
    {
        Assert.Null(DigestChallenge.Parse(["Basic realm=\"x\""]));
        Assert.Null(DigestChallenge.Parse(["Digest realm=\"x\", nonce=\"n\", algorithm=SHA-512-256"]));
    }

    [Fact]
    public void Authorization_WithoutQop_UsesTheOlderForm()
    {
        var challenge = DigestChallenge.Parse(["Digest realm=\"r\", nonce=\"n\""])!;

        var header = challenge.Authorization(new DigestCredentials("u", "p"), "GET", "/", "c");

        Assert.DoesNotContain("qop=", header);
        Assert.True(challenge.IsAnswerable);
    }
}
