using Missive.Application.Requests;
using Missive.Domain.Requests;

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
    public void FormFields_MapBothWaysAndDropBlankRows()
    {
        var dto = RequestDtoFactory.Create() with
        {
            Body = new BodyDto("form", "ignored", Fields:
            [
                new FormFieldDto("meta", "{}", ContentType: " application/json "),
                new FormFieldDto("file", "a.png", "file", Enabled: false, FileName: "b.png"),
                new FormFieldDto("", ""),
            ]),
        };

        var domain = RequestMapper.ToDomain(dto);

        Assert.True(domain.IsSuccess, domain.Error?.Message);
        Assert.Equal(
            RequestBody.WithFields(BodyType.Form,
            [
                new FormField("meta", "{}", ContentType: "application/json"),
                FormField.File("file", "a.png", fileName: "b.png", enabled: false),
            ]),
            domain.Value.Body);
        Assert.Equal(
            [new FormFieldDto("meta", "{}", ContentType: "application/json"), new FormFieldDto("file", "a.png", "file", false, FileName: "b.png")],
            RequestMapper.ToDto(domain.Value).Body.Fields);
    }

    [Fact]
    public void FormFields_OnlyUrlEncodedFieldsAreEncodedAndOnlyFormFieldsAreFiles()
    {
        var dto = RequestDtoFactory.Create() with
        {
            Body = new BodyDto("urlencoded", "", Fields: [new FormFieldDto("a", "b c", "file", ContentType: "text/plain", Encode: true)]),
        };

        var body = RequestMapper.ToDomain(dto).Value.Body;

        Assert.Equal(RequestBody.WithFields(BodyType.UrlEncoded, [new FormField("a", "b c", Encode: true)]), body);
    }

    [Fact]
    public void FormBodies_WithoutFieldsKeepTheirText()
    {
        var dto = RequestDtoFactory.Create() with { Body = new BodyDto("form", "a=1") };

        var request = RequestMapper.ToDomain(dto).Value;

        Assert.Equal(new RequestBody(BodyType.Form, "a=1"), request.Body);
        Assert.Null(RequestMapper.ToDto(request).Body.Fields);
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
