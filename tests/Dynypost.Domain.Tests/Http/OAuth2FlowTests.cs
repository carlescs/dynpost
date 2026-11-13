using System.Text;
using Dynypost.Domain.Http;
using Dynypost.Domain.Requests;

namespace Dynypost.Domain.Tests.Http;

public class OAuth2FlowTests
{
    private static readonly Dictionary<string, string> Variables = new()
    {
        ["auth"] = "https://auth.example.com",
        ["clientSecret"] = "s3cr3t",
    };

    private static readonly OAuth2Auth ClientCredentials = new(
        OAuth2Grant.ClientCredentials,
        TokenUrl: "{{auth}}/oauth/token",
        ClientId: "dynypost",
        ClientSecret: "{{clientSecret}}",
        Scope: "  read   write ",
        Audience: "https://api.example.com");

    private static readonly OAuth2Auth AuthorizationCode = new(
        OAuth2Grant.AuthorizationCode,
        TokenUrl: "{{auth}}/oauth/token",
        ClientId: "dynypost",
        AuthUrl: "{{auth}}/authorize?prompt=login",
        Scope: "openid");

    private static OAuth2Settings Settings(OAuth2Auth auth)
    {
        var resolution = OAuth2Flow.Resolve(auth, Variables);
        Assert.True(resolution.IsValid, string.Join(" ", resolution.Errors));
        return resolution.Settings!;
    }

    private static string Body(OutgoingRequest request) => ((TextBody)request.Body!).Content;

    private static string? Header(OutgoingRequest request, string name) =>
        request.Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    [Fact]
    public void Resolve_ResolvesVariablesAndNormalizesTheScope()
    {
        var settings = Settings(ClientCredentials);

        Assert.Equal("https://auth.example.com/oauth/token", settings.TokenUrl.AbsoluteUri);
        Assert.Equal("s3cr3t", settings.ClientSecret);
        Assert.Equal("read write", settings.Scope);
        Assert.Null(settings.AuthUrl);
    }

    [Fact]
    public void Resolve_KeepsTheSecretVerbatim()
    {
        var settings = Settings(ClientCredentials with { ClientSecret = " {{clientSecret}} " });

        Assert.Equal(" s3cr3t ", settings.ClientSecret);
    }

    [Fact]
    public void Resolve_ReportsMissingVariablesAndFields()
    {
        var resolution = OAuth2Flow.Resolve(new OAuth2Auth(OAuth2Grant.AuthorizationCode, TokenUrl: "{{nope}}/token"), Variables);

        Assert.False(resolution.IsValid);
        Assert.Equal(["nope"], resolution.MissingVariables);
        Assert.Contains(resolution.Errors, e => e.Contains("nope", StringComparison.Ordinal));
        Assert.Contains(resolution.Errors, e => e.Contains("authorization URL", StringComparison.Ordinal));
        Assert.Contains(resolution.Errors, e => e.Contains("client id", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://127.0.0.1:8765/callback")]
    [InlineData("http://example.com:8765/callback")]
    [InlineData("not a url")]
    public void Resolve_RefusesRedirectsOffThisComputer(string redirect)
    {
        var resolution = OAuth2Flow.Resolve(AuthorizationCode with { RedirectUri = redirect }, Variables);

        Assert.False(resolution.IsValid);
        Assert.Contains(resolution.Errors, e => e.Contains("redirect URL", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8765/callback")]
    [InlineData("http://localhost:3000/oauth")]
    [InlineData("http://[::1]:3000/")]
    public void Resolve_AcceptsLoopbackRedirects(string redirect)
    {
        Assert.Equal(new Uri(redirect), Settings(AuthorizationCode with { RedirectUri = redirect }).RedirectUri);
    }

    [Fact]
    public void ClientCredentialsRequest_SendsTheSecretAsBasicAuth()
    {
        var request = OAuth2Flow.ClientCredentialsRequest(Settings(ClientCredentials));

        Assert.Equal("POST", request.Method);
        Assert.Equal("https://auth.example.com/oauth/token", request.Url.AbsoluteUri);
        Assert.Equal("grant_type=client_credentials&scope=read+write&audience=https%3A%2F%2Fapi.example.com", Body(request));
        Assert.Equal("application/x-www-form-urlencoded", ((TextBody)request.Body!).DefaultMediaType);
        Assert.Equal("application/json", Header(request, "Accept"));
        Assert.Equal($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("dynypost:s3cr3t"))}", Header(request, "Authorization"));
    }

    [Fact]
    public void ClientCredentialsRequest_CanSendTheSecretInTheBody()
    {
        var request = OAuth2Flow.ClientCredentialsRequest(Settings(ClientCredentials with
        {
            Credentials = OAuth2ClientAuthentication.Body,
            Scope = string.Empty,
            Audience = string.Empty,
        }));

        Assert.Equal("grant_type=client_credentials&client_id=dynypost&client_secret=s3cr3t", Body(request));
        Assert.Null(Header(request, "Authorization"));
    }

    [Fact]
    public void TokenRequests_WithoutASecret_SendTheClientIdInTheBody()
    {
        var request = OAuth2Flow.RefreshRequest(Settings(AuthorizationCode), "r/1");

        Assert.Equal("grant_type=refresh_token&refresh_token=r%2F1&scope=openid&client_id=dynypost", Body(request));
        Assert.Null(Header(request, "Authorization"));
    }

    [Fact]
    public void AuthorizationUrl_KeepsTheEndpointsQueryAndAddsPkce()
    {
        var url = OAuth2Flow.AuthorizationUrl(Settings(AuthorizationCode), new Uri("http://127.0.0.1:5000/callback"), "st", "ch");

        Assert.Equal(
            "https://auth.example.com/authorize?prompt=login&response_type=code&client_id=dynypost"
            + "&redirect_uri=http%3A%2F%2F127.0.0.1%3A5000%2Fcallback&scope=openid&state=st&code_challenge=ch&code_challenge_method=S256",
            url.AbsoluteUri);
    }

    [Fact]
    public void AuthorizationCodeRequest_SendsTheCodeAndVerifier()
    {
        var request = OAuth2Flow.AuthorizationCodeRequest(Settings(AuthorizationCode), "c", new Uri("http://127.0.0.1:5000/callback"), "v");

        Assert.Equal(
            "grant_type=authorization_code&code=c&redirect_uri=http%3A%2F%2F127.0.0.1%3A5000%2Fcallback&code_verifier=v&client_id=dynypost",
            Body(request));
    }

    [Fact]
    public void CodeChallenge_MatchesTheRfc7636Example()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", OAuth2Flow.CodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void CacheKey_DependsOnWhoTheTokenIsForNotTheSecret()
    {
        var settings = Settings(ClientCredentials);

        Assert.Equal(settings.CacheKey, (settings with { ClientSecret = "other" }).CacheKey);
        Assert.NotEqual(settings.CacheKey, (settings with { Scope = "read" }).CacheKey);
        Assert.NotEqual(settings.CacheKey, (settings with { ClientId = "other" }).CacheKey);
        Assert.Equal(16, settings.CacheKey.Length);
    }

    [Fact]
    public void Prepare_SendsTheAccessTokenAsABearerToken()
    {
        var request = new HttpRequestDefinition("Test", RequestMethod.Get, "https://api.example.com", auth: ClientCredentials);

        var prepared = RequestPreparation.Prepare(request, Variables, "tok");

        Assert.True(prepared.IsValid);
        Assert.Equal("Bearer tok", Header(prepared.Request!, "Authorization"));
    }

    [Fact]
    public void Prepare_WithoutAnAccessToken_IsAnError()
    {
        var request = new HttpRequestDefinition("Test", RequestMethod.Get, "https://api.example.com", auth: ClientCredentials);

        var prepared = RequestPreparation.Prepare(request, Variables);

        Assert.False(prepared.IsValid);
        Assert.Contains(prepared.Errors, e => e.Contains("Get new token", StringComparison.Ordinal));
    }
}
