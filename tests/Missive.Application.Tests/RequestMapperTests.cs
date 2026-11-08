using Missive.Application.Requests;

namespace Missive.Application.Tests;

public class RequestMapperTests
{
    [Fact]
    public void Description_RoundTripsWithLfLineEndings()
    {
        var dto = RequestDtoFactory.Create() with { Description = "# Notes\r\n\r\nUse *staging*.\r\n" };

        var domain = RequestMapper.ToDomain(dto);

        Assert.True(domain.IsSuccess, domain.Error?.Message);
        Assert.Equal("# Notes\n\nUse *staging*.\n", domain.Value.Description);
        Assert.Equal(domain.Value.Description, RequestMapper.ToDto(domain.Value).Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n\t\n")]
    public void Description_BlankIsNone(string? description)
    {
        var domain = RequestMapper.ToDomain(RequestDtoFactory.Create() with { Description = description });

        Assert.Equal(string.Empty, domain.Value.Description);
    }

    [Fact]
    public void OAuth2_KeepsTheClientSecretVerbatim()
    {
        var dto = RequestDtoFactory.Create() with
        {
            Auth = new AuthDto("oauth2", Grant: "clientCredentials", TokenUrl: " https://auth.test/token ", ClientId: "app", ClientSecret: " s p ace "),
        };

        var auth = Assert.IsType<Missive.Domain.Requests.OAuth2Auth>(RequestMapper.ToDomain(dto).Value.Auth);

        Assert.Equal(" s p ace ", auth.ClientSecret);
        Assert.Equal("https://auth.test/token", auth.TokenUrl);
    }
}
