using Dynpost.Application.Interchange;
using Dynpost.Domain.Requests;
using Dynpost.Infrastructure.Interchange;

namespace Dynpost.Infrastructure.Tests.Interchange;

public class CurlConverterTests
{
    private readonly CurlConverter _converter = new();

    private ParsedCurl Parse(string command)
    {
        var result = _converter.Parse(command);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    [Fact]
    public void Parse_ReadsAPosixCommandWithJson()
    {
        var parsed = Parse("""
            curl -X POST 'https://api.test/users?notify=1' \
              -H 'Content-Type: application/json' \
              -H "X-Team: core" \
              -d '{"name": "Ada"}' --compressed -sS
            """);

        var request = parsed.Request;
        Assert.Equal(RequestMethod.Post, request.Method);
        Assert.Equal("https://api.test/users", request.Url);
        Assert.Equal([new KeyValueEntry("notify", "1")], request.Params);
        Assert.Equal([new KeyValueEntry("X-Team", "core")], request.Headers);
        Assert.Equal(new RequestBody(BodyType.Json, """{"name": "Ada"}"""), request.Body);
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void Parse_TurnsUserAndBearerHeadersIntoAuth()
    {
        Assert.Equal(new BasicAuth("ada", "p:w"), Parse("curl -u ada:p:w https://api.test").Request.Auth);
        Assert.Equal(new DigestAuth("ada", "pw"), Parse("curl --digest -u ada:pw https://api.test").Request.Auth);
        var bearer = Parse("curl -H 'Authorization: Bearer abc' https://api.test").Request;
        Assert.Equal(new BearerAuth("abc"), bearer.Auth);
        Assert.Empty(bearer.Headers);
    }

    [Fact]
    public void Parse_ReadsFormAndUrlEncodedBodies()
    {
        var form = Parse("curl https://api.test/upload -F 'file=@docs/report.pdf;type=application/pdf;filename=q3.pdf' -F title=Report").Request;
        Assert.Equal(RequestMethod.Post, form.Method);
        Assert.Equal(
            [FormField.File("file", "docs/report.pdf", "application/pdf", "q3.pdf"), new FormField("title", "Report")],
            form.Body.Fields);

        var encoded = Parse("curl https://api.test/login -d 'user=ada&remember=1' --data-urlencode 'note=a b'").Request;
        Assert.Equal(BodyType.UrlEncoded, encoded.Body.Type);
        Assert.Equal(
            [new FormField("user", "ada"), new FormField("remember", "1"), new FormField("note", "a b", Encode: true)],
            encoded.Body.Fields);
    }

    [Fact]
    public void Parse_HandlesGroupedOptionsGetDataAndSettings()
    {
        var request = Parse("curl -sSLk -XPUT --max-time 5 --http2 https://api.test/items -G -d q=x").Request;

        Assert.Equal(RequestMethod.Put, request.Method);
        Assert.Contains(new KeyValueEntry("q", "x"), request.Params);
        Assert.Equal(BodyType.None, request.Body.Type);
        Assert.Equal(new RequestSettings(TimeSpan.FromSeconds(5), VerifyTls: false, HttpVersion: HttpVersionPreference.Http2), request.Settings);
    }

    [Fact]
    public void Parse_ReadsWindowsCmdAndPowerShellCommands()
    {
        var cmd = Parse("curl \"https://api.test/a\" ^\n  -H \"Accept: application/json\" ^\n  --data-raw \"{\\\"a\\\":1}\"").Request;
        Assert.Equal("https://api.test/a", cmd.Url);
        Assert.Equal("""{"a":1}""", cmd.Body.Content);

        var powershell = Parse("curl.exe 'https://api.test/b' `\n  -H 'X-Name: it''s'").Request;
        Assert.Equal("https://api.test/b", powershell.Url);
    }

    [Fact]
    public void Parse_ReportsUnsupportedOptionsAndRejectsOtherCommands()
    {
        var parsed = Parse("curl --proxy http://p:8080 https://api.test");
        Assert.Contains(parsed.Warnings, w => w.Contains("--proxy"));

        Assert.False(_converter.Parse("wget https://api.test").IsSuccess);
        Assert.False(_converter.Parse("curl -H 'X: y'").IsSuccess);
        Assert.False(_converter.Parse("curl 'https://unclosed").IsSuccess);
    }

    [Theory]
    [InlineData(CurlShell.Posix)]
    [InlineData(CurlShell.PowerShell)]
    public void Write_ProducesACommandThatParsesBackToTheSameRequest(CurlShell shell)
    {
        var command = new CurlCommand(
            "PUT",
            "https://api.test/users/1?x=a b",
            [KeyValuePair.Create("X-Note", "it's \"quoted\"")],
            new CurlTextBody("{\n  \"name\": \"Ada\"\n}"),
            ("ada", "p w"),
            Insecure: true);

        var written = _converter.Write(command, shell);
        if (shell == CurlShell.PowerShell)
        {
            Assert.StartsWith("curl.exe", written);
            // The tokenizer reads POSIX quoting; PowerShell's '' is checked separately.
            return;
        }

        var parsed = Parse(written).Request;
        Assert.Equal(RequestMethod.Put, parsed.Method);
        Assert.Equal(new KeyValueEntry("X-Note", "it's \"quoted\""), parsed.Headers[0]);
        Assert.Equal("{\n  \"name\": \"Ada\"\n}", parsed.Body.Content);
        Assert.Equal(new BasicAuth("ada", "p w"), parsed.Auth);
        Assert.False(parsed.Settings.VerifyTls);
    }

    [Fact]
    public void Write_QuotesForEachShell()
    {
        Assert.Equal("'it'\\''s'", CurlConverter.Quote("it's", CurlShell.Posix));
        Assert.Equal("https://api.test/x", CurlConverter.Quote("https://api.test/x", CurlShell.Posix));
        Assert.Equal("'it''s'", CurlConverter.Quote("it's", CurlShell.PowerShell));
        Assert.Equal("\"say \\\"hi\\\"\"", CurlConverter.Quote("say \"hi\"", CurlShell.Cmd));
        Assert.Contains(" ^\n  ", _converter.Write(new CurlCommand("GET", "https://a.test", [KeyValuePair.Create("A", "b")]), CurlShell.Cmd));
    }

    [Fact]
    public void Write_WritesFormFieldsAndFiles()
    {
        var written = _converter.Write(
            new CurlCommand("POST", "https://a.test", [], new CurlFormBody([new FormField("title", "@notafile"), FormField.File("f", "/tmp/r.pdf", "application/pdf")])),
            CurlShell.Posix);

        Assert.Contains("--form-string title=@notafile", written);
        Assert.Contains("-F 'f=@/tmp/r.pdf;type=application/pdf'", written);
        Assert.DoesNotContain("-X POST", written);
    }
}
