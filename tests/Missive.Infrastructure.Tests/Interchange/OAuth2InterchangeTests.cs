using Missive.Application.Interchange;
using Missive.Domain.Requests;
using Missive.Infrastructure.Interchange;

namespace Missive.Infrastructure.Tests.Interchange;

/// <summary>OAuth 2.0 auth read from and written to other apps' formats.</summary>
public class OAuth2InterchangeTests
{
    private readonly CollectionImporter _importer = new();

    private ParsedCollection Parse(string text, string fileName = "export.json")
    {
        var result = _importer.Parse(text, fileName);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    [Fact]
    public void Postman_ImportsOAuth2Grants()
    {
        var parsed = Parse("""
            {
              "info": { "name": "Auth", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "auth": { "type": "oauth2", "oauth2": [
                { "key": "grant_type", "value": "client_credentials" },
                { "key": "accessTokenUrl", "value": "https://auth.test/token" },
                { "key": "clientId", "value": "app" },
                { "key": "clientSecret", "value": "{{clientSecret}}" },
                { "key": "scope", "value": "read" },
                { "key": "client_authentication", "value": "body" }
              ] },
              "item": [
                { "name": "Inherits", "request": { "method": "GET", "url": "https://x.test/a" } },
                { "name": "User", "request": { "method": "GET", "url": "https://x.test/b", "auth": { "type": "oauth2", "oauth2": [
                  { "key": "grant_type", "value": "authorization_code_with_pkce" },
                  { "key": "authUrl", "value": "https://auth.test/authorize" },
                  { "key": "accessTokenUrl", "value": "https://auth.test/token" },
                  { "key": "redirect_uri", "value": "https://oauth.pstmn.io/v1/callback" },
                  { "key": "clientId", "value": "app" }
                ] } } },
                { "name": "Password", "request": { "method": "GET", "url": "https://x.test/c", "auth": { "type": "oauth2", "oauth2": [
                  { "key": "grant_type", "value": "password_credentials" }
                ] } } }
              ]
            }
            """);

        var requests = parsed.Collection.Root.Requests;
        Assert.Equal(
            new OAuth2Auth(OAuth2Grant.ClientCredentials, "https://auth.test/token", "app", "{{clientSecret}}", Scope: "read", Credentials: OAuth2ClientAuthentication.Body),
            requests[0].Auth);
        Assert.Equal(
            new OAuth2Auth(OAuth2Grant.AuthorizationCode, "https://auth.test/token", "app", AuthUrl: "https://auth.test/authorize"),
            requests[1].Auth);
        Assert.Contains(parsed.Warnings, w => w.Contains("oauth.pstmn.io", StringComparison.Ordinal));
        Assert.Equal(RequestAuth.None, requests[2].Auth);
        Assert.Contains(parsed.Warnings, w => w.Contains("password credentials grant", StringComparison.Ordinal));
    }

    [Fact]
    public void Insomnia_ImportsOAuth2()
    {
        var parsed = Parse("""
            {
              "_type": "export", "__export_format": 4,
              "resources": [
                { "_id": "wrk_1", "_type": "workspace", "parentId": null, "name": "Billing" },
                { "_id": "req_1", "_type": "request", "parentId": "wrk_1", "name": "Me",
                  "method": "GET", "url": "https://x.test/me",
                  "authentication": { "type": "oauth2", "grantType": "authorization_code",
                    "authorizationUrl": "https://auth.test/authorize", "accessTokenUrl": "{{ _.tokenUrl }}",
                    "redirectUrl": "http://localhost:8765/cb", "clientId": "app", "clientSecret": "{{ _.secret }}",
                    "scope": "openid", "audience": "api", "usePkce": true } }
              ]
            }
            """);

        Assert.Equal(
            new OAuth2Auth(
                OAuth2Grant.AuthorizationCode,
                "{{tokenUrl}}",
                "app",
                "{{secret}}",
                "https://auth.test/authorize",
                "http://localhost:8765/cb",
                "openid",
                "api"),
            Assert.Single(parsed.Collection.Root.Requests).Auth);
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void OpenApi_ImportsOAuth2FlowsWithTheRequiredScopes()
    {
        var parsed = Parse("""
            openapi: 3.0.3
            info: { title: Shop, version: "1" }
            servers: [{ url: https://shop.test }]
            security: [{ oauth: [orders.read] }]
            paths:
              /orders:
                get: { responses: { "200": { description: ok } } }
            components:
              securitySchemes:
                oauth:
                  type: oauth2
                  flows:
                    authorizationCode: { authorizationUrl: /authorize, tokenUrl: /token, scopes: { orders.read: read } }
            """, "shop.yaml");

        Assert.Equal(
            new OAuth2Auth(
                OAuth2Grant.AuthorizationCode,
                "{{baseUrl}}/token",
                "{{clientId}}",
                "{{clientSecret}}",
                "{{baseUrl}}/authorize",
                Scope: "orders.read"),
            Assert.Single(parsed.Collection.Root.Requests).Auth);
        var variables = Assert.Single(parsed.Collection.Environments).Variables;
        Assert.Contains(variables, v => v is { Name: "clientSecret", Secret: true });
    }

    private static readonly RequestCollection Collection = new(
        new CollectionFolder("api", [], [
            new HttpRequestDefinition("Service", RequestMethod.Get, "https://x.test/a",
                auth: new OAuth2Auth(OAuth2Grant.ClientCredentials, "https://auth.test/token", "app", "{{secret}}", Scope: "read", Audience: "api",
                    Credentials: OAuth2ClientAuthentication.Body)),
            new HttpRequestDefinition("User", RequestMethod.Get, "https://x.test/b",
                auth: new OAuth2Auth(OAuth2Grant.AuthorizationCode, "https://auth.test/token", "app", AuthUrl: "https://auth.test/authorize",
                    RedirectUri: "http://127.0.0.1:8765/callback")),
        ]),
        []);

    [Fact]
    public void Postman_RoundTripsOAuth2()
    {
        var output = new PostmanCollectionExporter().Export(Collection);

        var parsed = Parse(output.Content, "api.postman_collection.json");

        Assert.Equal(Collection.Root.Requests.Select(r => r.Auth), parsed.Collection.Root.Requests.Select(r => r.Auth));
    }

    [Fact]
    public void Insomnia_RoundTripsOAuth2()
    {
        var output = new InsomniaExporter().Export(Collection);

        var parsed = Parse(output.Content);

        Assert.Equal(Collection.Root.Requests.Select(r => r.Auth), parsed.Collection.Root.Requests.Select(r => r.Auth));
    }
}
