using Dynypost.Domain.Http;

namespace Dynypost.Domain.Tests.Http;

public class ResponseFileNameTests
{
    [Theory]
    [InlineData("attachment; filename=\"report.pdf\"", "report.pdf")]
    [InlineData("attachment; filename=plain.txt", "plain.txt")]
    [InlineData("attachment; filename=\"a;b.csv\"", "a;b.csv")]
    [InlineData("attachment; filename=\"fallback.txt\"; filename*=UTF-8''caf%C3%A9%20menu.txt", "café menu.txt")]
    [InlineData("inline; FILENAME*=iso-8859-1''na%EFve.txt", "naïve.txt")]
    [InlineData("attachment; filename=\"../../etc/passwd\"", "passwd")]
    [InlineData("attachment; filename=\"C:\\\\temp\\\\evil.exe\"", "evil.exe")]
    [InlineData("attachment; filename=\"what?.txt\"", "what_.txt")]
    public void Suggest_UsesContentDisposition(string header, string expected) =>
        Assert.Equal(expected, ResponseFileName.Suggest(header, new Uri("https://example.com/download"), "application/octet-stream"));

    [Theory]
    [InlineData("https://example.com/files/photo.jpg", "image/jpeg", "photo.jpg")]
    [InlineData("https://example.com/users", "application/json", "users.json")]
    [InlineData("https://example.com/users/", "application/vnd.api+json", "users.json")]
    [InlineData("https://example.com/my%20file", "text/plain", "my file.txt")]
    [InlineData("https://example.com/", "text/html", "response.html")]
    [InlineData("https://example.com/v1.2", "application/json", "v1.2")]
    [InlineData("https://example.com/data", null, "data")]
    [InlineData("https://example.com/", "application/x-unknown", "response")]
    public void Suggest_FallsBackToTheUrlAndMediaType(string url, string? mediaType, string expected) =>
        Assert.Equal(expected, ResponseFileName.Suggest(null, new Uri(url), mediaType));

    [Fact]
    public void Suggest_IgnoresContentDispositionWithoutAName() =>
        Assert.Equal("report.pdf", ResponseFileName.Suggest("attachment; filename=\"..\"", new Uri("https://x.test/report"), "application/pdf"));

    [Fact]
    public void Suggest_ShortensVeryLongNamesButKeepsTheExtension()
    {
        var name = ResponseFileName.Suggest($"attachment; filename=\"{new string('a', 300)}.json\"", null, null);

        Assert.Equal(120, name.Length);
        Assert.EndsWith(".json", name);
    }
}
