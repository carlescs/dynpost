using Dynpost.Domain.Http;

namespace Dynpost.Domain.Tests.Http;

public class SecretMaskingTests
{
    private const string M = SecretMasking.Mask;

    [Theory]
    [InlineData("Authorization", "Bearer abc", "Bearer " + M)]
    [InlineData("authorization", "Basic dXNlcjpwYXNz", "Basic " + M)]
    [InlineData("Proxy-Authorization", "opaque", M)]
    [InlineData("X-Api-Key", "k-123", M)]
    [InlineData("X-Trace", "id=s3cret-long", "id=" + M)]
    [InlineData("Accept", "application/json", "application/json")]
    public void MaskHeader_HidesCredentialsAndSecrets(string name, string value, string expected) =>
        Assert.Equal(expected, SecretMasking.MaskHeader(name, value, ["x-api-key"], ["s3cret", "s3cret-long"]));

    [Fact]
    public void MaskUrl_HidesSensitiveParametersPasswordsAndSecrets()
    {
        var url = new Uri("https://user:pa55@api.example.com/s3cret/items?api_key=k1&page=2&q=s%2Fcret#top");

        var masked = SecretMasking.MaskUrl(url, ["api_key"], ["s3cret", "s/cret"]);

        Assert.Equal($"https://user:{M}@api.example.com/{M}/items?api_key={M}&page=2&q={M}#top", masked);
    }

    [Fact]
    public void MaskSecrets_IgnoresEmptySecrets() =>
        Assert.Equal("abc", SecretMasking.MaskSecrets("abc", ["", "zzz"]));
}
