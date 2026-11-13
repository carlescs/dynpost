using System.Text;
using Dynypost.Domain.Http;
using Dynypost.Domain.Requests;

namespace Dynypost.Domain.Tests.Http;

public class RequestPreparationTests
{
    private static readonly Dictionary<string, string> NoVariables = [];

    private static OutgoingRequest PrepareValid(HttpRequestDefinition request, Dictionary<string, string>? variables = null)
    {
        var prepared = RequestPreparation.Prepare(request, variables ?? NoVariables);
        Assert.True(prepared.IsValid, string.Join(" ", prepared.Errors));
        return prepared.Request!;
    }

    private static IReadOnlyList<string> PrepareInvalid(HttpRequestDefinition request)
    {
        var prepared = RequestPreparation.Prepare(request, NoVariables);
        Assert.False(prepared.IsValid);
        Assert.NotEmpty(prepared.Errors);
        return prepared.Errors;
    }

    private static HttpRequestDefinition Get(string url) => new("Test", RequestMethod.Get, url);

    [Fact]
    public void Prepare_AppendsEnabledParamsToTheQuery()
    {
        var request = Get("https://example.com/search?sort=asc") with
        {
            Params =
            [
                new KeyValueEntry("q", "a b&c"),
                new KeyValueEntry("debug", "true", Enabled: false),
            ],
        };

        var outgoing = PrepareValid(request);

        Assert.Equal("GET", outgoing.Method);
        Assert.Equal("https://example.com/search?sort=asc&q=a%20b%26c", outgoing.Url.AbsoluteUri);
    }

    [Fact]
    public void Prepare_ResolvesVariablesEverywhere()
    {
        var request = Get("{{baseUrl}}/users") with
        {
            Params = [new KeyValueEntry("page", "{{page}}")],
            Headers = [new KeyValueEntry("X-Tenant", "{{tenant}}")],
            Body = new RequestBody(BodyType.Json, """{"page": {{page}}}"""),
        };
        var variables = new Dictionary<string, string> { ["baseUrl"] = "https://api.test", ["page"] = "2", ["tenant"] = "acme" };

        var outgoing = PrepareValid(request, variables);

        Assert.Equal("https://api.test/users?page=2", outgoing.Url.AbsoluteUri);
        Assert.Contains(KeyValuePair.Create("X-Tenant", "acme"), outgoing.Headers);
        Assert.Equal(new TextBody("""{"page": 2}""", "application/json"), outgoing.Body);
    }

    [Fact]
    public void Prepare_ReportsUndefinedVariablesOnce()
    {
        var request = Get("{{baseUrl}}/users") with { Auth = new BearerAuth("{{apiToken}}") };

        var errors = PrepareInvalid(request);

        Assert.Equal("The request uses variables that aren't defined: apiToken, baseUrl.", Assert.Single(errors));
    }

    [Fact]
    public void Prepare_AddsHttpWhenTheUrlHasNoScheme()
    {
        Assert.Equal("http://localhost:5000/health", PrepareValid(Get("localhost:5000/health")).Url.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.com/file")]
    [InlineData("http://")]
    public void Prepare_RejectsMissingOrNonHttpUrls(string url)
    {
        PrepareInvalid(Get(url));
    }

    [Fact]
    public void Prepare_LeavesOutDisabledHeaders()
    {
        var request = Get("https://example.com") with
        {
            Headers = [new KeyValueEntry("Accept", "application/json"), new KeyValueEntry("X-Debug", "1", Enabled: false)],
        };

        Assert.Equal([KeyValuePair.Create("Accept", "application/json")], PrepareValid(request).Headers);
    }

    [Theory]
    [InlineData("Bad Header")]
    [InlineData("X:Y")]
    public void Prepare_RejectsInvalidHeaderNames(string name)
    {
        PrepareInvalid(Get("https://example.com") with { Headers = [new KeyValueEntry(name, "1")] });
    }

    [Fact]
    public void Prepare_AddsBasicAuth()
    {
        var outgoing = PrepareValid(Get("https://example.com") with { Auth = new BasicAuth("ada", "sécret") });

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("ada:sécret"));
        Assert.Contains(KeyValuePair.Create("Authorization", $"Basic {expected}"), outgoing.Headers);
    }

    [Fact]
    public void Prepare_AddsBearerAuth()
    {
        var outgoing = PrepareValid(Get("https://example.com") with { Auth = new BearerAuth("{{token}}") },
            new Dictionary<string, string> { ["token"] = "abc" });

        Assert.Contains(KeyValuePair.Create("Authorization", "Bearer abc"), outgoing.Headers);
    }

    [Fact]
    public void Prepare_RequiresABearerToken()
    {
        PrepareInvalid(Get("https://example.com") with { Auth = new BearerAuth(" ") });
    }

    [Fact]
    public void Prepare_AnExplicitAuthorizationHeaderWins()
    {
        var request = Get("https://example.com") with
        {
            Headers = [new KeyValueEntry("authorization", "Custom xyz")],
            Auth = new BearerAuth("abc"),
        };

        Assert.Equal([KeyValuePair.Create("authorization", "Custom xyz")], PrepareValid(request).Headers);
    }

    [Fact]
    public void Prepare_AddsAnApiKeyAsHeaderOrQueryParameter()
    {
        var header = PrepareValid(Get("https://example.com") with { Auth = new ApiKeyAuth("X-Api-Key", "k1") });
        var query = PrepareValid(Get("https://example.com/x") with { Auth = new ApiKeyAuth("api_key", "k 2", ApiKeyLocation.Query) });

        Assert.Contains(KeyValuePair.Create("X-Api-Key", "k1"), header.Headers);
        Assert.Equal("https://example.com/x?api_key=k%202", query.Url.AbsoluteUri);
        Assert.Empty(query.Headers);
    }

    [Fact]
    public void Prepare_HasNoBodyForBodyTypeNone()
    {
        Assert.Null(PrepareValid(Get("https://example.com")).Body);
    }

    [Fact]
    public void Prepare_BuildsTextBodies()
    {
        var post = new HttpRequestDefinition("Test", RequestMethod.Post, "https://example.com");

        Assert.Equal(new TextBody("hi", "text/plain"), PrepareValid(post with { Body = new RequestBody(BodyType.Text, "hi") }).Body);
        Assert.Equal(
            new TextBody("a=1&b=two%20words", "application/x-www-form-urlencoded"),
            PrepareValid(post with { Body = new RequestBody(BodyType.UrlEncoded, "a=1\n\n  b=two%20words \n") }).Body);
    }

    [Fact]
    public void Prepare_BuildsGraphqlPostAndResolvesQueryAndVariables()
    {
        var request = new HttpRequestDefinition(
            "GraphQL",
            RequestMethod.Get,
            "https://example.com/graphql",
            body: new RequestBody(BodyType.Graphql, "query { user(id: {{id}}) }", """{"tenant":"{{tenant}}"}"""));
        var variables = new Dictionary<string, string> { ["id"] = "42", ["tenant"] = "acme" };

        var outgoing = PrepareValid(request, variables);

        Assert.Equal("POST", outgoing.Method);
        Assert.Equal(
            new TextBody("""{"query":"query { user(id: 42) }","variables":{"tenant":"acme"}}""", "application/json"),
            outgoing.Body);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{")]
    public void Prepare_RejectsInvalidGraphqlVariables(string variables)
    {
        var request = new HttpRequestDefinition(
            "GraphQL",
            RequestMethod.Post,
            "https://example.com/graphql",
            body: new RequestBody(BodyType.Graphql, "query { me }", variables));

        Assert.Contains("GraphQL variables must be a valid JSON object.", PrepareInvalid(request));
    }

    [Fact]
    public void Prepare_BuildsMultipartFieldsFromLines()
    {
        var post = new HttpRequestDefinition("Test", RequestMethod.Post, "https://example.com",
            body: new RequestBody(BodyType.Form, "name = Ada\nnote=a=b\nflag\n"));

        var body = Assert.IsType<MultipartBody>(PrepareValid(post).Body);

        Assert.Equal(
            [new MultipartField("name", "Ada"), new MultipartField("note", "a=b"), new MultipartField("flag", "")],
            body.Fields);
    }

    [Fact]
    public void Prepare_ResolvesBinaryAndMultipartFilesAgainstWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var request = new HttpRequestDefinition(
            "Upload",
            RequestMethod.Post,
            "https://example.com",
            body: new RequestBody(BodyType.Form, "title=Ada\navatar=@fixtures/ada.png;type=image/png"));

        var prepared = RequestPreparation.Prepare(request, NoVariables, workspaceRoot: root);

        Assert.True(prepared.IsValid, string.Join(" ", prepared.Errors));
        Assert.Equal(
            [
                new MultipartField("title", "Ada"),
                new MultipartField("avatar", FilePath: Path.Combine(root, "fixtures", "ada.png"), ContentType: "image/png"),
            ],
            Assert.IsType<MultipartBody>(prepared.Request!.Body).Fields);

        var binary = RequestPreparation.Prepare(request with { Body = new RequestBody(BodyType.Binary, "fixtures/ada.png") },
            NoVariables, workspaceRoot: root);
        Assert.True(binary.IsValid, string.Join(" ", binary.Errors));
        Assert.Equal(new BinaryBody(Path.Combine(root, "fixtures", "ada.png")), binary.Request!.Body);
    }

    [Fact]
    public void Prepare_BuildsMultipartPartsFromFieldsInOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var request = new HttpRequestDefinition("Upload", RequestMethod.Post, "https://example.com",
            body: RequestBody.WithFields(BodyType.Form,
            [
                new FormField("meta", "{\"id\": {{id}}}", ContentType: "application/json"),
                FormField.File("file", "a.png", fileName: "{{name}}.png"),
                new FormField("skipped", "x", Enabled: false),
                FormField.File("file", "/abs/b.pdf", "application/pdf"),
                new FormField(""),
            ]));
        var variables = new Dictionary<string, string> { ["id"] = "7", ["name"] = "avatar" };

        var prepared = RequestPreparation.Prepare(request, variables, workspaceRoot: root);

        Assert.True(prepared.IsValid, string.Join(" ", prepared.Errors));
        Assert.Equal(
            [
                new MultipartField("meta", "{\"id\": 7}", ContentType: "application/json"),
                new MultipartField("file", FilePath: Path.Combine(root, "a.png"), FileName: "avatar.png"),
                new MultipartField("file", FilePath: Path.GetFullPath("/abs/b.pdf"), ContentType: "application/pdf"),
            ],
            Assert.IsType<MultipartBody>(prepared.Request!.Body).Fields);
    }

    [Fact]
    public void Prepare_ReportsFileFieldsWithoutAFileAndInvalidContentTypes()
    {
        var request = new HttpRequestDefinition("Upload", RequestMethod.Post, "https://example.com",
            body: RequestBody.WithFields(BodyType.Form,
            [
                FormField.File("avatar", ""),
                new FormField("meta", "{}", ContentType: "not a type"),
                new FormField("", "orphan"),
            ]));

        var errors = PrepareInvalid(request);

        Assert.Contains("Choose a file for the form field 'avatar'.", errors);
        Assert.Contains("'not a type' is not a valid multipart content type.", errors);
        Assert.Contains("Enter a name for every form field.", errors);
    }

    [Fact]
    public void Prepare_SendsUrlEncodedFieldsAsWrittenOrEncoded()
    {
        var request = new HttpRequestDefinition("Log in", RequestMethod.Post, "https://example.com",
            body: RequestBody.WithFields(BodyType.UrlEncoded,
            [
                new FormField("user", "ada@example.com", Encode: true),
                new FormField("raw", "a%20b"),
                new FormField("off", "1", Enabled: false),
                new FormField("note", "{{note}}", Encode: true),
            ]));

        var body = PrepareValid(request, new Dictionary<string, string> { ["note"] = "a&b" }).Body;

        Assert.Equal(new TextBody("user=ada%40example.com&raw=a%20b&note=a%26b", "application/x-www-form-urlencoded"), body);
    }

    [Fact]
    public void FormFields_ParseTheTextOfOlderFiles()
    {
        Assert.Equal(
            [new FormField("a", "1"), FormField.File("f", "x.png", "image/png"), new FormField("flag")],
            FormFields.ParseText("a = 1\n\nf=@x.png;type=image/png\nflag\n", BodyType.Form));
        Assert.Equal(
            [new FormField("a", "1"), new FormField("b", "@x")],
            FormFields.ParseText("a=1&b=@x", BodyType.UrlEncoded));
        Assert.Empty(FormFields.Of(new RequestBody(BodyType.Json, "a=1")));
    }

    [Fact]
    public void RequestFilePaths_StoresPickedFilesRelativeToTheWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Equal(("docs/a.png", false), RequestFilePaths.ForRequest(Path.Combine(root, "docs", "a.png"), root));
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere.png");
        Assert.Equal((Path.GetFullPath(outside), true), RequestFilePaths.ForRequest(outside, root));
        Assert.Equal((Path.GetFullPath(outside), true), RequestFilePaths.ForRequest(outside, null));
    }

    [Fact]
    public void FileMediaTypes_ComeFromTheExtension()
    {
        Assert.Equal("image/png", FileMediaTypes.ForPath("a/b.PNG"));
        Assert.Equal("application/pdf", FileMediaTypes.ForPath("report.pdf"));
        Assert.Equal(FileMediaTypes.Default, FileMediaTypes.ForPath("archive.unknown"));
    }

    [Fact]
    public void Prepare_RejectsRelativeFilePathsOutsideWorkspace()
    {
        var prepared = RequestPreparation.Prepare(
            new HttpRequestDefinition("Upload", RequestMethod.Post, "https://example.com",
                body: new RequestBody(BodyType.Binary, "../outside.bin")),
            NoVariables,
            workspaceRoot: Path.GetTempPath());

        Assert.False(prepared.IsValid);
        Assert.Contains(prepared.Errors, error => error.Contains("must stay inside the workspace"));
    }
}
