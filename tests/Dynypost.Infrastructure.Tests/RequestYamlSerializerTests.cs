using Dynypost.Domain.Requests;
using Dynypost.Infrastructure.Yaml;

namespace Dynypost.Infrastructure.Tests;

public class RequestYamlSerializerTests
{
    [Fact]
    public void Serialize_WritesKeysInStableOrderAndOmitsDefaults()
    {
        var request = new HttpRequestDefinition("Get user", RequestMethod.Get, "https://api.example.com/users/1");

        Assert.Equal(
            """
            name: Get user
            method: GET
            url: https://api.example.com/users/1

            """.ReplaceLineEndings("\n"),
            RequestYamlSerializer.Serialize(request));
    }

    [Fact]
    public void Serialize_WritesTheDocumentedFormat()
    {
        var request = new HttpRequestDefinition(
            "Create user",
            RequestMethod.Post,
            "{{baseUrl}}/users",
            @params: [new KeyValueEntry("page", "1"), new KeyValueEntry("debug", "true", Enabled: false)],
            headers: [new KeyValueEntry("Content-Type", "application/json")],
            auth: new BearerAuth("{{apiToken}}"),
            body: new RequestBody(BodyType.Json, "{\n  \"name\": \"Ada Lovelace\"\n}\n"));

        Assert.Equal(
            """
            name: Create user
            method: POST
            url: "{{baseUrl}}/users"
            params:
              page: "1"
              ~debug: "true"
            headers:
              Content-Type: application/json
            auth:
              type: bearer
              token: "{{apiToken}}"
            body:
              type: json
              content: |
                {
                  "name": "Ada Lovelace"
                }

            """.ReplaceLineEndings("\n"),
            RequestYamlSerializer.Serialize(request));
    }

    [Fact]
    public void GraphqlBodies_WriteAndReadQueryAndVariablesSeparately()
    {
        var request = new HttpRequestDefinition(
            "GraphQL",
            RequestMethod.Post,
            "https://api.example.com/graphql",
            body: new RequestBody(BodyType.Graphql, "query { me { id } }", """{"id":"{{userId}}"}"""));

        var yaml = RequestYamlSerializer.Serialize(request);

        Assert.Equal(
            """
            name: GraphQL
            method: POST
            url: https://api.example.com/graphql
            body:
              type: graphql
              query: query { me { id } }
              variables: "{\"id\":\"{{userId}}\"}"

            """.ReplaceLineEndings("\n"),
            yaml);
        Assert.Equal(request.Body, RequestYamlSerializer.Deserialize(yaml, "fallback").Body);
    }

    [Fact]
    public void FormBodies_WriteFieldsAsAListAndReadThemBack()
    {
        var request = new HttpRequestDefinition(
            "Upload",
            RequestMethod.Post,
            "https://api.example.com/upload",
            body: RequestBody.WithFields(BodyType.Form,
            [
                new FormField("metadata", "{\"title\": \"Report\"}", ContentType: "application/json"),
                FormField.File("file", "uploads/report.pdf", fileName: "q3-report.pdf"),
                FormField.File("file", ""),
                new FormField("note", "line 1\nline 2"),
                new FormField("debug", "true", Enabled: false),
                new FormField("empty"),
            ]));

        var yaml = RequestYamlSerializer.Serialize(request);

        Assert.Equal(
            """
            name: Upload
            method: POST
            url: https://api.example.com/upload
            body:
              type: form
              fields:
                - name: metadata
                  value: "{\"title\": \"Report\"}"
                  contentType: application/json
                - name: file
                  file: uploads/report.pdf
                  fileName: q3-report.pdf
                - name: file
                  file: ""
                - name: note
                  value: |-
                    line 1
                    line 2
                - name: debug
                  value: "true"
                  disabled: true
                - name: empty

            """.ReplaceLineEndings("\n"),
            yaml);
        Assert.Equal(request.Body, RequestYamlSerializer.Deserialize(yaml, "fallback").Body);
    }

    [Fact]
    public void UrlEncodedBodies_WriteTheEncodeFlag()
    {
        var request = new HttpRequestDefinition(
            "Log in",
            RequestMethod.Post,
            "https://api.example.com/login",
            body: RequestBody.WithFields(BodyType.UrlEncoded, [new FormField("user", "ada@example.com", Encode: true), new FormField("remember", "1")]));

        var yaml = RequestYamlSerializer.Serialize(request);

        Assert.Contains("  fields:\n    - name: user\n      value: ada@example.com\n      encode: true\n    - name: remember\n      value: \"1\"\n", yaml);
        Assert.Equal(request.Body, RequestYamlSerializer.Deserialize(yaml, "fallback").Body);
    }

    [Fact]
    public void FormBodies_OfOlderFilesKeepTheirText()
    {
        const string yaml = "name: Upload\nmethod: POST\nurl: https://x\nbody:\n  type: form\n  content: |-\n    title=Hi\n    file=@a.png\n";

        var request = RequestYamlSerializer.Deserialize(yaml, "fallback");

        Assert.Equal(new RequestBody(BodyType.Form, "title=Hi\nfile=@a.png"), request.Body);
        Assert.Equal(yaml, RequestYamlSerializer.Serialize(request));
        Assert.Equal([new FormField("title", "Hi"), FormField.File("file", "a.png")], FormFields.Of(request.Body));
    }

    [Theory]
    [InlineData("body:\n  type: form\n  fields: nope\n")]
    [InlineData("body:\n  type: form\n  fields:\n    - just text\n")]
    [InlineData("body:\n  type: form\n  fields:\n    - name: a\n      disabled: maybe\n")]
    public void FormBodies_RejectInvalidFields(string yaml)
    {
        Assert.Throws<RequestFormatException>(() => RequestYamlSerializer.Deserialize(yaml, "fallback"));
    }

    [Fact]
    public void BinaryBodies_RoundTripFilePaths()
    {
        var request = new HttpRequestDefinition(
            "Upload",
            RequestMethod.Post,
            "https://api.example.com/upload",
            body: new RequestBody(BodyType.Binary, "./fixtures/archive.bin"));

        var yaml = RequestYamlSerializer.Serialize(request);

        Assert.Contains("body:\n  type: binary\n  content: ./fixtures/archive.bin\n", yaml);
        Assert.Equal(request.Body, RequestYamlSerializer.Deserialize(yaml, "fallback").Body);
    }

    [Fact]
    public void Captures_AreWrittenAfterTheBodyAndReadBack()
    {
        var request = new HttpRequestDefinition("Log in", RequestMethod.Post, "/login")
        {
            Captures = [new CaptureRule("token", "$.access_token"), new CaptureRule("etag", "header:ETag")],
            Description = "Logs in.",
        };

        var yaml = RequestYamlSerializer.Serialize(request);

        Assert.Equal(
            """
            name: Log in
            method: POST
            url: /login
            capture:
              token: $.access_token
              etag: header:ETag
            description: Logs in.

            """.ReplaceLineEndings("\n"),
            yaml);
        Assert.Equal(request.Captures, RequestYamlSerializer.Deserialize(yaml, "x").Captures);
        Assert.Throws<RequestFormatException>(() => RequestYamlSerializer.Deserialize("capture: [a]\n", "x"));
    }

    [Fact]
    public void Serialize_WritesTheDescriptionLastAsABlock()
    {
        var request = new HttpRequestDefinition("Get user", RequestMethod.Get, "/users/1", body: new RequestBody(BodyType.Text, "x"))
        {
            Description = "# Get user\n\nReturns *one* user.\n",
        };

        var yaml = RequestYamlSerializer.Serialize(request);

        Assert.Equal(
            """
            name: Get user
            method: GET
            url: /users/1
            body:
              type: text
              content: x
            description: |
              # Get user

              Returns *one* user.

            """.ReplaceLineEndings("\n"),
            yaml);
        Assert.Equal(request.Description, RequestYamlSerializer.Deserialize(yaml, "x").Description);
        Assert.Equal(string.Empty, RequestYamlSerializer.Deserialize("name: a\n", "x").Description);
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("")]
    [InlineData("  leading and trailing  ")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("~")]
    [InlineData("0x1F")]
    [InlineData("1.5e3")]
    [InlineData("key: value")]
    [InlineData("# not a comment")]
    [InlineData("value # not a comment")]
    [InlineData("- item")]
    [InlineData("'quoted'")]
    [InlineData("\"double\" \\ backslash")]
    [InlineData("tab\there")]
    [InlineData("no trailing newline\nsecond line")]
    [InlineData("two trailing newlines\n\n")]
    [InlineData("\nleading newline")]
    [InlineData("  indented first line\nsecond")]
    [InlineData("windows\r\nline endings")]
    [InlineData("blank\n\n  lines inside\n")]
    [InlineData("ünïcödé ✓")]
    public void RoundTrip_PreservesValuesExactly(string value)
    {
        var request = new HttpRequestDefinition(
            "Round trip",
            RequestMethod.Put,
            value,
            headers: [new KeyValueEntry("X-Value", value), new KeyValueEntry(value.Length == 0 || value.StartsWith('~') ? "key" + value : value, "k")],
            auth: new BasicAuth("user", value.Length == 0 ? "p" : value),
            body: new RequestBody(BodyType.Text, value));

        var yaml = RequestYamlSerializer.Serialize(request);
        var parsed = RequestYamlSerializer.Deserialize(yaml, "fallback");

        Assert.Equal(request.Url, parsed.Url);
        Assert.Equal(request.Headers, parsed.Headers);
        Assert.Equal(request.Auth, parsed.Auth);
        Assert.Equal(request.Body, parsed.Body);
        Assert.Equal(yaml, RequestYamlSerializer.Serialize(parsed));
    }

    [Fact]
    public void Deserialize_ReadsHandWrittenFilesLeniently()
    {
        var parsed = RequestYamlSerializer.Deserialize(
            """
            # a comment
            url: http://localhost:5000/health
            method: get
            headers:
              Retry: 3
              Empty:
            auth:
              type: apiKey
              key: X-API-Key
              value: secret
              in: query
            """,
            "health");

        Assert.Equal("health", parsed.Name);
        Assert.Equal(RequestMethod.Get, parsed.Method);
        Assert.Equal([new KeyValueEntry("Retry", "3"), new KeyValueEntry("Empty", "")], parsed.Headers);
        Assert.Equal(new ApiKeyAuth("X-API-Key", "secret", ApiKeyLocation.Query), parsed.Auth);
    }

    [Fact]
    public void OAuth2Auth_RoundTripsInTheDocumentedKeyOrder()
    {
        var request = new HttpRequestDefinition(
            "Me",
            RequestMethod.Get,
            "https://api.test/me",
            auth: new OAuth2Auth(
                OAuth2Grant.AuthorizationCode,
                "https://auth.test/token",
                "dynypost",
                "{{clientSecret}}",
                "https://auth.test/authorize",
                "http://127.0.0.1:8765/callback",
                "openid email",
                "https://api.test",
                OAuth2ClientAuthentication.Body));

        var yaml = RequestYamlSerializer.Serialize(request);

        Assert.Equal(
            """
            name: Me
            method: GET
            url: https://api.test/me
            auth:
              type: oauth2
              grant: authorizationCode
              authUrl: https://auth.test/authorize
              tokenUrl: https://auth.test/token
              redirectUri: http://127.0.0.1:8765/callback
              clientId: dynypost
              clientSecret: "{{clientSecret}}"
              scope: openid email
              audience: https://api.test
              credentials: body

            """.ReplaceLineEndings("\n"),
            yaml);
        Assert.Equal(request.Auth, RequestYamlSerializer.Deserialize(yaml, "me").Auth);
    }

    [Fact]
    public void OAuth2Auth_OmitsEmptyAndDefaultFields()
    {
        var request = new HttpRequestDefinition("S", RequestMethod.Get, "https://api.test",
            auth: new OAuth2Auth(OAuth2Grant.ClientCredentials, "https://auth.test/token", "app"));

        var yaml = RequestYamlSerializer.Serialize(request);

        Assert.EndsWith("auth:\n  type: oauth2\n  grant: clientCredentials\n  tokenUrl: https://auth.test/token\n  clientId: app\n", yaml);
        Assert.Equal(request.Auth, RequestYamlSerializer.Deserialize(yaml, "s").Auth);
    }

    [Fact]
    public void OAuth2Auth_UnknownGrant_IsAFormatError()
    {
        var error = Assert.Throws<RequestFormatException>(() => RequestYamlSerializer.Deserialize("auth:\n  type: oauth2\n  grant: implicit\n", "x"));

        Assert.Contains("implicit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name: [unclosed")]
    [InlineData("- just\n- a list")]
    [InlineData("name: x\nmethod: FETCH")]
    [InlineData("name: x\nheaders: [a, b]")]
    [InlineData("name: x\nbody:\n  type: xml")]
    [InlineData("name: x\nname: y")]
    [InlineData("")]
    public void Deserialize_RejectsInvalidFiles(string yaml)
    {
        Assert.Throws<RequestFormatException>(() => RequestYamlSerializer.Deserialize(yaml, "x"));
    }

    [Fact]
    public void Auth_IsOmittedWhenInheritedAndWrittenAsNoneWhenTurnedOff()
    {
        var inheriting = new HttpRequestDefinition("Ping", RequestMethod.Get, "/ping");
        var none = inheriting with { Auth = RequestAuth.None };

        Assert.DoesNotContain("auth", RequestYamlSerializer.Serialize(inheriting));
        Assert.Equal("name: Ping\nmethod: GET\nurl: /ping\nauth:\n  type: none\n", RequestYamlSerializer.Serialize(none));
        Assert.Same(RequestAuth.Inherit, RequestYamlSerializer.Deserialize("name: Ping\n", "ping").Auth);
        Assert.Same(RequestAuth.Inherit, RequestYamlSerializer.Deserialize("auth:\n  type: inherit\n", "ping").Auth);
        Assert.Same(RequestAuth.None, RequestYamlSerializer.Deserialize("auth:\n  type: none\n", "ping").Auth);
    }

    [Fact]
    public void FolderDefaults_RoundTripWithHeadersBeforeAuth()
    {
        var defaults = new FolderDefaults(
            [new KeyValueEntry("Accept", "application/json"), new KeyValueEntry("X-Debug", "1", Enabled: false)],
            new BearerAuth("{{apiToken}}"));

        var yaml = RequestYamlSerializer.SerializeFolder(defaults);

        Assert.Equal(
            """
            headers:
              Accept: application/json
              ~X-Debug: "1"
            auth:
              type: bearer
              token: "{{apiToken}}"

            """.ReplaceLineEndings("\n"),
            yaml);
        var parsed = RequestYamlSerializer.DeserializeFolder(yaml);
        Assert.Equal(defaults.Headers, parsed.Headers);
        Assert.Equal(defaults.Auth, parsed.Auth);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# nothing yet\n")]
    public void FolderDefaults_AnEmptyFileSetsNothing(string yaml)
    {
        Assert.True(RequestYamlSerializer.DeserializeFolder(yaml).IsEmpty);
    }

    [Fact]
    public void FolderDefaults_RejectsInvalidFiles()
    {
        Assert.Throws<RequestFormatException>(() => RequestYamlSerializer.DeserializeFolder("- a list\n"));
        Assert.Throws<RequestFormatException>(() => RequestYamlSerializer.DeserializeFolder("auth:\n  type: magic\n"));
    }
}
