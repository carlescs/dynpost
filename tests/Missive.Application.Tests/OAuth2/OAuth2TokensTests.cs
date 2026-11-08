using System.Text;
using Missive.Application.Common;
using Missive.Application.Http;
using Missive.Application.OAuth2;
using Missive.Domain.Http;
using Missive.Domain.Requests;
using NSubstitute;

namespace Missive.Application.Tests.OAuth2;

using Tokens = IReadOnlyDictionary<string, IReadOnlyDictionary<string, OAuth2Token>>;

public class OAuth2TokensTests
{
    private const string Root = "/work/my-api";
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly IHttpSender _sender = Substitute.For<IHttpSender>();
    private readonly IOAuth2TokenStore _store = Substitute.For<IOAuth2TokenStore>();
    private readonly IAuthorizationBrowser _browser = Substitute.For<IAuthorizationBrowser>();
    private readonly TimeProvider _time = Substitute.For<TimeProvider>();
    private readonly OAuth2Tokens _tokens;
    private Dictionary<string, IReadOnlyDictionary<string, OAuth2Token>> _stored = new();

    private static readonly OAuth2Settings ClientCredentials = OAuth2Flow.Resolve(
        new OAuth2Auth(OAuth2Grant.ClientCredentials, "https://auth.example.com/token", "app", "secret", Scope: "read"),
        new Dictionary<string, string>()).Settings!;

    private static readonly OAuth2Settings AuthorizationCode = OAuth2Flow.Resolve(
        new OAuth2Auth(OAuth2Grant.AuthorizationCode, "https://auth.example.com/token", "app", AuthUrl: "https://auth.example.com/authorize"),
        new Dictionary<string, string>()).Settings!;

    public OAuth2TokensTests()
    {
        _time.GetUtcNow().Returns(Now);
        _store.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(_ => Result<Tokens>.Success(_stored));
        _store.SaveAsync(Root, Arg.Any<Tokens>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _stored = call.Arg<Tokens>().ToDictionary(e => e.Key, e => e.Value);
            return Unit.Value;
        });
        _tokens = new OAuth2Tokens(_sender, _store, _browser, _time);
    }

    private static HttpResponseData Json(string json, int status = 200) =>
        new(status, status == 200 ? "OK" : "Bad Request", [KeyValuePair.Create("Content-Type", "application/json")], Encoding.UTF8.GetBytes(json), false, TimeSpan.Zero);

    private void Cache(OAuth2Settings settings, OAuth2Token token, string environment = "dev.yaml") =>
        _stored[environment] = new Dictionary<string, OAuth2Token> { [settings.CacheKey] = token };

    private static OAuth2Token Token(string access, DateTimeOffset? expires, string? refresh = null) =>
        new("https://auth.example.com/token", "app", "read", access, refresh, expires, Now.AddHours(-1));

    private static string Body(OutgoingRequest request) => ((TextBody)request.Body!).Content;

    [Fact]
    public async Task Get_UsesAValidCachedToken()
    {
        Cache(ClientCredentials, Token("cached", Now.AddMinutes(5)));

        var token = await _tokens.GetAsync(ClientCredentials, Root, "dev.yaml");

        Assert.Equal("cached", token.Value.AccessToken);
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!);
    }

    [Fact]
    public async Task Get_ClientCredentials_FetchesAndCachesANewToken()
    {
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(Json("""{"access_token":"new","token_type":"Bearer","expires_in":3600}"""));

        var token = await _tokens.GetAsync(ClientCredentials, Root, "dev.yaml");

        Assert.Equal("new", token.Value.AccessToken);
        Assert.Equal(Now.AddHours(1), token.Value.Expires);
        Assert.Equal("read", token.Value.Scope);
        Assert.Equal("new", _stored["dev.yaml"][ClientCredentials.CacheKey].AccessToken);
        await _sender.Received(1).SendAsync(
            Arg.Is<OutgoingRequest>(r => Body(r).StartsWith("grant_type=client_credentials", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ExpiredToken_IsRefreshedKeepingTheRefreshToken()
    {
        Cache(AuthorizationCode, Token("old", Now.AddSeconds(10), refresh: "r1"));
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(Json("""{"access_token":"fresh","expires_in":"60"}"""));

        var token = await _tokens.GetAsync(AuthorizationCode, Root, "dev.yaml");

        Assert.Equal("fresh", token.Value.AccessToken);
        Assert.Equal("r1", token.Value.RefreshToken);
        await _sender.Received(1).SendAsync(
            Arg.Is<OutgoingRequest>(r => Body(r).Contains("grant_type=refresh_token&refresh_token=r1", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await _browser.DidNotReceiveWithAnyArgs().AuthorizeAsync(default, default!);
    }

    [Fact]
    public async Task Get_RefusedRefresh_AsksToSignInAgainAndForgetsTheToken()
    {
        Cache(AuthorizationCode, Token("old", Now.AddMinutes(-1), refresh: "r1"));
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(Json("""{"error":"invalid_grant","error_description":"Refresh token expired"}""", 400));

        var token = await _tokens.GetAsync(AuthorizationCode, Root, "dev.yaml");

        Assert.Equal(OAuth2Errors.SignInRequiredCode, token.Error?.Code);
        Assert.Contains("expired", token.Error!.Message, StringComparison.Ordinal);
        Assert.Empty(_stored["dev.yaml"]);
    }

    [Fact]
    public async Task Get_ServerFailureDuringRefresh_KeepsTheRefreshToken()
    {
        Cache(AuthorizationCode, Token("old", Now.AddMinutes(-1), refresh: "r1"));
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(Json("unavailable", 503));

        var token = await _tokens.GetAsync(AuthorizationCode, Root, "dev.yaml");

        Assert.Equal(OAuth2Errors.FailedCode, token.Error?.Code);
        Assert.Equal("r1", _stored["dev.yaml"][AuthorizationCode.CacheKey].RefreshToken);
    }

    [Fact]
    public async Task Get_ConcurrentRequestsRefreshOnce()
    {
        Cache(AuthorizationCode, Token("old", Now.AddMinutes(-1), refresh: "r1"));
        var release = new TaskCompletionSource();
        var calls = 0;
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            Interlocked.Increment(ref calls);
            await release.Task;
            return Result<HttpResponseData>.Success(Json("""{"access_token":"fresh","refresh_token":"r2","expires_in":3600}"""));
        });

        var first = _tokens.GetAsync(AuthorizationCode, Root, "dev.yaml");
        var second = _tokens.GetAsync(AuthorizationCode, Root, "dev.yaml");
        release.SetResult();

        Assert.Equal("fresh", (await first).Value.AccessToken);
        Assert.Equal("fresh", (await second).Value.AccessToken);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Parse_ZeroLifetime_IsAlreadyExpired()
    {
        var token = OAuth2TokenResponse.Parse(Json("""{"access_token":"a","expires_in":0}"""), ClientCredentials, Now).Value;

        Assert.Equal(Now, token.Expires);
        Assert.True(token.IsExpired(Now));
    }

    [Fact]
    public void Parse_InvalidGrant_IsARejectedGrant()
    {
        var result = OAuth2TokenResponse.Parse(Json("""{"error":"invalid_grant"}""", 400), ClientCredentials, Now);

        Assert.Equal(OAuth2Errors.GrantRejectedCode, result.Error?.Code);
    }

    [Fact]
    public async Task Get_AuthorizationCodeWithoutToken_NeverOpensTheBrowser()
    {
        var token = await _tokens.GetAsync(AuthorizationCode, Root, "dev.yaml");

        Assert.Equal(OAuth2Errors.SignInRequiredCode, token.Error?.Code);
        await _browser.DidNotReceiveWithAnyArgs().AuthorizeAsync(default, default!);
    }

    [Fact]
    public async Task Get_TokensArePerEnvironment()
    {
        Cache(ClientCredentials, Token("prod", Now.AddMinutes(5)), "prod.yaml");
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(Json("""{"access_token":"dev"}"""));

        var token = await _tokens.GetAsync(ClientCredentials, Root, "dev.yaml");

        Assert.Equal("dev", token.Value.AccessToken);
        Assert.Null(token.Value.Expires);
    }

    [Fact]
    public async Task Get_WithoutWorkspace_CachesInMemory()
    {
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(Json("""{"access_token":"mem"}"""));

        await _tokens.GetAsync(ClientCredentials, null, null);
        var again = await _tokens.GetAsync(ClientCredentials, null, null);

        Assert.Equal("mem", again.Value.AccessToken);
        await _sender.Received(1).SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!);
    }

    [Fact]
    public async Task RequestNew_AuthorizationCode_SignsInAndExchangesTheCode()
    {
        var redirect = new Uri("http://127.0.0.1:5000/callback");
        Uri? opened = null;
        _browser.AuthorizeAsync(null, Arg.Any<Func<Uri, Uri>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            opened = call.Arg<Func<Uri, Uri>>()(redirect);
            var state = Query(opened)["state"];
            return new AuthorizationCallback(redirect, new Dictionary<string, string> { ["code"] = "c1", ["state"] = state });
        });
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(Json("""{"access_token":"user","refresh_token":"r","expires_in":300,"scope":"openid email"}"""));

        var token = await _tokens.RequestNewAsync(AuthorizationCode, Root, "dev.yaml");

        Assert.Equal("user", token.Value.AccessToken);
        Assert.Equal("openid email", token.Value.Scope);
        var query = Query(opened!);
        Assert.Equal("S256", query["code_challenge_method"]);
        await _sender.Received(1).SendAsync(
            Arg.Is<OutgoingRequest>(r => Body(r).Contains("code=c1", StringComparison.Ordinal)
                && Body(r).Contains("code_verifier=", StringComparison.Ordinal)
                && OAuth2Flow.CodeChallenge(Field(Body(r), "code_verifier")) == query["code_challenge"]),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestNew_RejectsAnAnswerWithTheWrongState()
    {
        _browser.AuthorizeAsync(null, Arg.Any<Func<Uri, Uri>>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationCallback(new Uri("http://127.0.0.1:1/callback"), new Dictionary<string, string> { ["code"] = "c", ["state"] = "forged" }));

        var token = await _tokens.RequestNewAsync(AuthorizationCode, Root, "dev.yaml");

        Assert.Equal(OAuth2Errors.FailedCode, token.Error?.Code);
        Assert.Contains("state", token.Error!.Message, StringComparison.Ordinal);
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!);
    }

    [Fact]
    public async Task RequestNew_ReportsAnAuthorizationError()
    {
        _browser.AuthorizeAsync(null, Arg.Any<Func<Uri, Uri>>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationCallback(new Uri("http://127.0.0.1:1/callback"), new Dictionary<string, string> { ["error"] = "access_denied" }));

        var token = await _tokens.RequestNewAsync(AuthorizationCode, Root, "dev.yaml");

        Assert.Equal("Sign-in failed: access_denied.", token.Error?.Message);
    }

    [Fact]
    public async Task RequestNew_NetworkErrorsKeepTheirCode()
    {
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(HttpErrors.NetworkError("auth.example.com", "refused"));

        var token = await _tokens.RequestNewAsync(ClientCredentials, Root, "dev.yaml");

        Assert.Equal(HttpErrors.NetworkErrorCode, token.Error?.Code);
        Assert.StartsWith("Could not get an OAuth 2.0 token.", token.Error!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(401, """{"error":"invalid_client"}""", "The token endpoint refused the request (401 Bad Request): invalid_client.")]
    [InlineData(500, "oops", "The token endpoint answered 500 Bad Request.")]
    [InlineData(200, """{"token_type":"bearer"}""", "The token endpoint's answer has no access token (200 OK).")]
    public void Parse_ExplainsFailures(int status, string body, string message)
    {
        var result = OAuth2TokenResponse.Parse(Json(body, status), ClientCredentials, Now);

        Assert.Equal(message, result.Error?.Message);
    }

    [Fact]
    public void Parse_ReadsFormEncodedAnswers()
    {
        var response = new HttpResponseData(
            200, "OK", [KeyValuePair.Create("Content-Type", "application/x-www-form-urlencoded")],
            Encoding.UTF8.GetBytes("access_token=a%2Bb&scope=repo&token_type=bearer"), false, TimeSpan.Zero);

        var token = OAuth2TokenResponse.Parse(response, ClientCredentials, Now).Value;

        Assert.Equal("a+b", token.AccessToken);
        Assert.Equal("repo", token.Scope);
    }

    private static Dictionary<string, string> Query(Uri url) =>
        url.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

    private static string Field(string form, string name) =>
        form.Split('&').Select(pair => pair.Split('=', 2)).First(p => p[0] == name)[1];
}
