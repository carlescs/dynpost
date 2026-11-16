using Dynpost.Application.Common;
using Dynpost.Application.Interchange;
using Dynpost.Domain.Requests;
using Dynpost.Infrastructure.Interchange;

namespace Dynpost.Infrastructure.Tests.Interchange;

public class JMeterImportTests
{
    private readonly CollectionImporter _importer = new();

    private ParsedCollection Parse(string text, string fileName = "shop.jmx")
    {
        var result = _importer.Parse(text, fileName);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    /// <summary>A test plan with <paramref name="body"/> as the children of the plan's hashTree.</summary>
    private static string Plan(string body, string planName = "Shop", string variables = "") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <jmeterTestPlan version="1.2" properties="5.0" jmeter="5.6.3">
          <hashTree>
            <TestPlan guiclass="TestPlanGui" testclass="TestPlan" testname="{planName}">
              <elementProp name="TestPlan.user_defined_variables" elementType="Arguments" guiclass="ArgumentsPanel" testclass="Arguments">
                <collectionProp name="Arguments.arguments">{variables}</collectionProp>
              </elementProp>
            </TestPlan>
            <hashTree>
              {body}
            </hashTree>
          </hashTree>
        </jmeterTestPlan>
        """;

    private static string ThreadGroup(string name, string body) => $"""
        <ThreadGroup guiclass="ThreadGroupGui" testclass="ThreadGroup" testname="{name}">
          <intProp name="ThreadGroup.num_threads">10</intProp>
          <elementProp name="ThreadGroup.main_controller" elementType="LoopController" guiclass="LoopControlPanel" testclass="LoopController">
            <stringProp name="LoopController.loops">5</stringProp>
          </elementProp>
        </ThreadGroup>
        <hashTree>
          {body}
        </hashTree>
        """;

    private static string Argument(string name, string value, bool encode = false) => $"""
        <elementProp name="{name}" elementType="HTTPArgument">
          <boolProp name="HTTPArgument.always_encode">{(encode ? "true" : "false")}</boolProp>
          <stringProp name="Argument.value">{value}</stringProp>
          <stringProp name="Argument.metadata">=</stringProp>
          <stringProp name="Argument.name">{name}</stringProp>
        </elementProp>
        """;

    private static string Sampler(string name, string method, string path, string extra = "", string arguments = "", string children = "", string domain = "") => $"""
        <HTTPSamplerProxy guiclass="HttpTestSampleGui" testclass="HTTPSamplerProxy" testname="{name}">
          <stringProp name="HTTPSampler.domain">{domain}</stringProp>
          <stringProp name="HTTPSampler.path">{path}</stringProp>
          <stringProp name="HTTPSampler.method">{method}</stringProp>
          <boolProp name="HTTPSampler.follow_redirects">true</boolProp>
          <elementProp name="HTTPsampler.Arguments" elementType="Arguments" guiclass="HTTPArgumentsPanel" testclass="Arguments">
            <collectionProp name="Arguments.arguments">{arguments}</collectionProp>
          </elementProp>
          {extra}
        </HTTPSamplerProxy>
        <hashTree>{children}</hashTree>
        """;

    private static string Headers(params (string Name, string Value)[] headers) => $"""
        <HeaderManager guiclass="HeaderPanel" testclass="HeaderManager" testname="HTTP Header Manager">
          <collectionProp name="HeaderManager.headers">
            {string.Concat(headers.Select(h => $"""
                <elementProp name="" elementType="Header">
                  <stringProp name="Header.name">{h.Name}</stringProp>
                  <stringProp name="Header.value">{h.Value}</stringProp>
                </elementProp>
                """))}
          </collectionProp>
        </HeaderManager>
        <hashTree/>
        """;

    private const string Defaults = """
        <ConfigTestElement guiclass="HttpDefaultsGui" testclass="ConfigTestElement" testname="HTTP Request Defaults">
          <elementProp name="HTTPsampler.Arguments" elementType="Arguments" guiclass="HTTPArgumentsPanel" testclass="Arguments">
            <collectionProp name="Arguments.arguments"/>
          </elementProp>
          <stringProp name="HTTPSampler.domain">${host}</stringProp>
          <stringProp name="HTTPSampler.port">8443</stringProp>
          <stringProp name="HTTPSampler.protocol">https</stringProp>
          <stringProp name="HTTPSampler.connect_timeout">5000</stringProp>
          <stringProp name="HTTPSampler.response_timeout">25000</stringProp>
        </ConfigTestElement>
        <hashTree/>
        """;

    private static string Variable(string name, string value) => $"""
        <elementProp name="{name}" elementType="Argument">
          <stringProp name="Argument.name">{name}</stringProp>
          <stringProp name="Argument.value">{value}</stringProp>
          <stringProp name="Argument.metadata">=</stringProp>
        </elementProp>
        """;

    [Fact]
    public void ReadsThreadGroupsAsFoldersAndSamplersAsRequests()
    {
        var parsed = Parse(Plan(
            Defaults + Headers(("Accept", "application/json")) + ThreadGroup("Browse", $"""
                {Sampler("List products", "GET", "/products?sort=name", arguments: Argument("page", "${page}"))}
                <TransactionController guiclass="TransactionControllerGui" testclass="TransactionController" testname="Checkout">
                  <boolProp name="TransactionController.includeTimers">false</boolProp>
                </TransactionController>
                <hashTree>
                  {Sampler("Add to cart", "POST", "/cart", arguments: Argument("id", "42") + Argument("note", "a b"))}
                </hashTree>
                """),
            variables: Variable("host", "shop.test") + Variable("page", "1")));

        Assert.Equal(JMeterFormat.FormatName, parsed.Format);
        Assert.Empty(parsed.Collection.Environments);

        var root = parsed.Collection.Root;
        Assert.Equal("Shop", root.Name);
        Assert.Equal([new KeyValueEntry("Accept", "application/json")], root.Defaults.Headers);
        Assert.Equal(
            [new KeyValueEntry("host", "shop.test"), new KeyValueEntry("page", "1"), new KeyValueEntry("baseUrl", "https://{{host}}:8443")],
            root.Defaults.Variables);
        Assert.Equal(TimeSpan.FromSeconds(30), root.Defaults.Settings.Timeout);

        var browse = Assert.Single(root.Folders);
        Assert.Equal("Browse", browse.Name);
        Assert.True(browse.Defaults.IsEmpty);
        var list = Assert.Single(browse.Requests);
        Assert.Equal(RequestMethod.Get, list.Method);
        Assert.Equal("{{baseUrl}}/products", list.Url);
        Assert.Equal([new KeyValueEntry("sort", "name"), new KeyValueEntry("page", "{{page}}")], list.Params);
        Assert.Equal(RequestBody.None, list.Body);
        Assert.Equal(RequestAuth.Inherit, list.Auth);

        var checkout = Assert.Single(browse.Folders);
        Assert.Equal("Checkout", checkout.Name);
        var add = Assert.Single(checkout.Requests);
        Assert.Equal(RequestMethod.Post, add.Method);
        Assert.Equal(BodyType.UrlEncoded, add.Body.Type);
        Assert.Equal([new FormField("id", "42"), new FormField("note", "a b")], add.Body.Fields);
        Assert.Empty(add.Params);

        // Load settings belong to run plans; nothing is said about the thread group itself.
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void ReadsRawBodiesAsJsonOrTextBySampleAndHeaders()
    {
        var raw = """<boolProp name="HTTPSampler.postBodyRaw">true</boolProp>""";
        var parsed = Parse(Plan(ThreadGroup("Users", $$"""
            {{Sampler("Create user", "POST", "/users", raw, Argument("", """{"name": "${name}", "id": "${__UUID()}"}"""),
                children: Headers(("Content-Type", "application/json"), ("X-Trace", "${__Random(1,99)}")), domain: "api.test")}}
            {{Sampler("Send note", "PUT", "/notes", raw, Argument("", "hello ${__time()}"), domain: "api.test")}}
            {{Sampler("Guess", "PATCH", "/guess", raw, Argument("", "[1, 2]"), domain: "api.test")}}
            """)));

        var users = Assert.Single(parsed.Collection.Root.Folders).Requests;
        var create = users[0];
        Assert.Equal("http://api.test/users", create.Url);
        Assert.Equal(new RequestBody(BodyType.Json, """{"name": "{{name}}", "id": "{{$uuid}}"}"""), create.Body);

        // The JSON content type is implied by the body; the other header stays.
        Assert.Equal([new KeyValueEntry("X-Trace", "{{$randomInt 1 99}}")], create.Headers);

        Assert.Equal(new RequestBody(BodyType.Text, "hello {{$timestamp}}000"), users[1].Body);
        Assert.Equal(BodyType.Json, users[2].Body.Type);
        Assert.Contains(parsed.Warnings, w => w.Contains("{{$timestamp}}000", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadsMultipartAndFileBodies()
    {
        var files = """
            <boolProp name="HTTPSampler.DO_MULTIPART_POST">true</boolProp>
            <elementProp name="HTTPsampler.Files" elementType="HTTPFileArgs">
              <collectionProp name="HTTPFileArgs.files">
                <elementProp name="/data/photo.png" elementType="HTTPFileArg">
                  <stringProp name="File.path">/data/photo.png</stringProp>
                  <stringProp name="File.paramname">photo</stringProp>
                  <stringProp name="File.mimetype">image/png</stringProp>
                </elementProp>
              </collectionProp>
            </elementProp>
            """;
        var binary = """
            <elementProp name="HTTPsampler.Files" elementType="HTTPFileArgs">
              <collectionProp name="HTTPFileArgs.files">
                <elementProp name="body.bin" elementType="HTTPFileArg">
                  <stringProp name="File.path">body.bin</stringProp>
                  <stringProp name="File.paramname"></stringProp>
                  <stringProp name="File.mimetype">application/octet-stream</stringProp>
                </elementProp>
              </collectionProp>
            </elementProp>
            """;
        var parsed = Parse(Plan(ThreadGroup("Files", $"""
            {Sampler("Upload", "POST", "/upload", files, Argument("title", "Cat"), domain: "x.test")}
            {Sampler("Put blob", "PUT", "/blob", binary, domain: "x.test")}
            """)));

        var requests = Assert.Single(parsed.Collection.Root.Folders).Requests;
        Assert.Equal(BodyType.Form, requests[0].Body.Type);
        Assert.Equal([new FormField("title", "Cat"), FormField.File("photo", "/data/photo.png", "image/png")], requests[0].Body.Fields);
        Assert.Equal(new RequestBody(BodyType.Binary, "body.bin"), requests[1].Body);
    }

    [Fact]
    public void ReadsAuthorizationManagers()
    {
        static string Auth(params (string Url, string User, string Mechanism)[] entries) => $"""
            <AuthManager guiclass="AuthPanel" testclass="AuthManager" testname="HTTP Authorization Manager">
              <collectionProp name="AuthManager.auth_list">
                {string.Concat(entries.Select(e => $"""
                    <elementProp name="" elementType="Authorization">
                      <stringProp name="Authorization.url">{e.Url}</stringProp>
                      <stringProp name="Authorization.username">{e.User}</stringProp>
                      <stringProp name="Authorization.password">${"{"}password{"}"}</stringProp>
                      <stringProp name="Authorization.domain"></stringProp>
                      <stringProp name="Authorization.realm"></stringProp>
                      <stringProp name="Authorization.mechanism">{e.Mechanism}</stringProp>
                    </elementProp>
                    """))}
              </collectionProp>
            </AuthManager>
            <hashTree/>
            """;

        var parsed = Parse(Plan(
            ThreadGroup("Everything", Auth(("", "admin", "BASIC_DIGEST")) + Sampler("Status", "GET", "/status", domain: "a.test"))
            + ThreadGroup("By URL", Auth(("http://a.test/admin", "root", "DIGEST"), ("http://b.test", "bob", "KERBEROS"))
                + Sampler("Admin", "GET", "/admin/users", domain: "a.test")
                + Sampler("Public", "GET", "/public", domain: "a.test"))));

        var everything = parsed.Collection.Root.Folders[0];
        Assert.Equal(new BasicAuth("admin", "{{password}}"), everything.Defaults.Auth);
        Assert.Equal(RequestAuth.Inherit, everything.Requests[0].Auth);

        var byUrl = parsed.Collection.Root.Folders[1];
        Assert.Equal(RequestAuth.Inherit, byUrl.Defaults.Auth);
        Assert.Equal(new DigestAuth("root", "{{password}}"), byUrl.Requests[0].Auth);
        Assert.Equal(RequestAuth.Inherit, byUrl.Requests[1].Auth);
        Assert.Contains(parsed.Warnings, w => w.Contains("KERBEROS", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadsJsonExtractorsAsCaptures()
    {
        static string Extractor(string names, string paths, string matches) => $"""
            <JSONPostProcessor guiclass="JSONPostProcessorGui" testclass="JSONPostProcessor" testname="Extract">
              <stringProp name="JSONPostProcessor.referenceNames">{names}</stringProp>
              <stringProp name="JSONPostProcessor.jsonPathExprs">{paths}</stringProp>
              <stringProp name="JSONPostProcessor.match_numbers">{matches}</stringProp>
            </JSONPostProcessor>
            <hashTree/>
            """;

        var parsed = Parse(Plan(ThreadGroup("Login", Sampler(
            "Sign in",
            "POST",
            "/login",
            domain: "x.test",
            children: Extractor("token;userId;all", "$.access_token;$.user.id;$..id", "1;0;-1")))));

        var request = Assert.Single(Assert.Single(parsed.Collection.Root.Folders).Requests);
        Assert.Equal([new CaptureRule("token", "$.access_token"), new CaptureRule("userId", "$.user.id")], request.Captures);
        Assert.Contains(parsed.Warnings, w => w.Contains("$..id", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadsSamplerSettingsAndComments()
    {
        var extra = """
            <boolProp name="HTTPSampler.follow_redirects">false</boolProp>
            <boolProp name="HTTPSampler.auto_redirects">false</boolProp>
            <stringProp name="HTTPSampler.response_timeout">2000</stringProp>
            <stringProp name="TestPlan.comments">Checks the *health* endpoint.</stringProp>
            """;
        var sampler = Sampler("Health", "HEAD", "https://status.test/health", extra);

        // The later follow_redirects wins over the one the helper writes.
        sampler = sampler.Replace("""<boolProp name="HTTPSampler.follow_redirects">true</boolProp>""", string.Empty, StringComparison.Ordinal);
        var parsed = Parse(Plan(sampler));

        var request = Assert.Single(parsed.Collection.Root.Requests);
        Assert.Equal(RequestMethod.Head, request.Method);
        Assert.Equal("https://status.test/health", request.Url);
        Assert.Equal(new RequestSettings(Timeout: TimeSpan.FromSeconds(2), FollowRedirects: false), request.Settings);
        Assert.Equal("Checks the *health* endpoint.", request.Description);
    }

    [Fact]
    public void RewritesPropertiesAndKeepsUnknownFunctionsAsText()
    {
        var parsed = Parse(Plan(ThreadGroup("Load", Sampler(
            "Search",
            "GET",
            "/search",
            domain: "${__P(host,search.test)}",
            arguments: Argument("q", "${__P(term)}") + Argument("n", "${__counter(FALSE,)}") + Argument("odd", "${my var}")))));

        Assert.Equal([new KeyValueEntry("host", "search.test")], parsed.Collection.Root.Defaults.Variables);
        var request = Assert.Single(Assert.Single(parsed.Collection.Root.Folders).Requests);
        Assert.Equal("http://{{host}}/search", request.Url);
        Assert.Equal(
            [new KeyValueEntry("q", "{{term}}"), new KeyValueEntry("n", "${__counter(FALSE,)}"), new KeyValueEntry("odd", "${my var}")],
            request.Params);
        Assert.Contains(parsed.Warnings, w => w.Contains("__counter in 'Search'", StringComparison.Ordinal));
        Assert.Contains(parsed.Warnings, w => w.Contains("${my var}", StringComparison.Ordinal));
        Assert.Contains(parsed.Warnings, w => w.Contains("set them in an environment: term.", StringComparison.Ordinal));
    }

    [Fact]
    public void ListsWhatItSkipsGroupedByReason()
    {
        var parsed = Parse(Plan(ThreadGroup("Mixed", $"""
            {Sampler("Kept", "GET", "/kept", domain: "x.test", children: """
                <ResponseAssertion guiclass="AssertionGui" testclass="ResponseAssertion" testname="Is 200"/>
                <hashTree/>
                <JSR223PostProcessor guiclass="TestBeanGUI" testclass="JSR223PostProcessor" testname="Parse token"/>
                <hashTree/>
                <RegexExtractor guiclass="RegexExtractorGui" testclass="RegexExtractor" testname="Find id"/>
                <hashTree/>
                """)}
            {Sampler("Off", "GET", "/off", domain: "x.test").Replace("testname=\"Off\"", "testname=\"Off\" enabled=\"false\"", StringComparison.Ordinal)}
            {Sampler("Trace", "TRACE", "/trace", domain: "x.test")}
            <IfController guiclass="IfControllerPanel" testclass="IfController" testname="If logged in"/>
            <hashTree>
              {Sampler("Profile", "GET", "/me", domain: "x.test")}
            </hashTree>
            <WhileController guiclass="WhileControllerGui" testclass="WhileController" testname="Empty loop"/>
            <hashTree/>
            <JDBCSampler guiclass="TestBeanGUI" testclass="JDBCSampler" testname="Query db"/>
            <hashTree/>
            <ConstantTimer guiclass="ConstantTimerGui" testclass="ConstantTimer" testname="Think"/>
            <hashTree/>
            <CookieManager guiclass="CookiePanel" testclass="CookieManager" testname="Cookies"/>
            <hashTree/>
            <ResultCollector guiclass="ViewResultsFullVisualizer" testclass="ResultCollector" testname="View Results Tree"/>
            <hashTree/>
            """)));

        var mixed = Assert.Single(parsed.Collection.Root.Folders);
        Assert.Equal(["Kept"], mixed.Requests.Select(r => r.Name));

        // The If controller becomes a folder; the empty While controller is dropped.
        var profile = Assert.Single(mixed.Folders);
        Assert.Equal("If logged in", profile.Name);

        var warnings = string.Join("\n", parsed.Warnings);
        Assert.Contains("Skipped disabled elements: 'Off' (HTTPSamplerProxy).", warnings);
        Assert.Contains("'Trace' (TRACE)", warnings);
        Assert.Contains("logic controllers: 'If logged in' (IfController), 'Empty loop' (WhileController).", warnings);
        Assert.Contains("Dynpost doesn't run scripts: 'Parse token' (JSR223PostProcessor).", warnings);
        Assert.Contains("only JSON Extractors become captures: 'Find id' (RegexExtractor).", warnings);
        Assert.Contains("load run plans: 'Is 200' (ResponseAssertion), 'Think' (ConstantTimer).", warnings);
        Assert.Contains("only imports HTTP requests: 'Query db' (JDBCSampler).", warnings);
        Assert.Contains("listeners", warnings);
        Assert.DoesNotContain("Cookies", warnings);
    }

    [Fact]
    public void NamesThePlanAfterTheFileWhenItKeepsTheDefaultName()
    {
        var parsed = Parse(Plan(Sampler("Ping", "GET", "/ping", domain: "x.test"), planName: "Test Plan"), "checkout-load.jmx");

        Assert.Equal("checkout-load", parsed.Collection.Root.Name);
    }

    [Fact]
    public void RenamesTheBaseUrlVariableWhenThePlanDefinesOne()
    {
        var parsed = Parse(Plan(
            Defaults + Sampler("Ping", "GET", "/ping"),
            variables: Variable("host", "x.test") + Variable("baseUrl", "/v2")));

        Assert.Equal("https://{{host}}:8443", parsed.Collection.Root.Defaults.Variables.Single(v => v.Key == "jmeterBaseUrl").Value);
        Assert.Equal("{{jmeterBaseUrl}}/ping", Assert.Single(parsed.Collection.Root.Requests).Url);
    }

    [Fact]
    public void ReportsSamplersWithoutAServer()
    {
        var parsed = Parse(Plan(Sampler("Ping", "GET", "ping")));

        Assert.Equal("/ping", Assert.Single(parsed.Collection.Root.Requests).Url);
        Assert.Contains(parsed.Warnings, w => w.StartsWith("Requests without a server name", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesDtdsAndOtherXml()
    {
        var withDtd = """
            <?xml version="1.0"?>
            <!DOCTYPE jmeterTestPlan [ <!ENTITY secret SYSTEM "file:///etc/passwd"> ]>
            <jmeterTestPlan><hashTree>&secret;</hashTree></jmeterTestPlan>
            """;
        Assert.Equal(ErrorCodes.InvalidFile, _importer.Parse(withDtd, "evil.jmx").Error?.Code);

        var other = _importer.Parse("<project><name>x</name></project>", "pom.xml");
        Assert.Equal(ErrorCodes.InvalidFile, other.Error?.Code);
        Assert.Contains("JMeter test plans", other.Error!.Message, StringComparison.Ordinal);

        Assert.Equal(ErrorCodes.InvalidFile, _importer.Parse("<jmeterTestPlan><hashTree/></jmeterTestPlan>", "empty.jmx").Error?.Code);
    }
}
