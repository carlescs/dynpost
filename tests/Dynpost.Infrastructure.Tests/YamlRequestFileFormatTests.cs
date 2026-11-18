using Dynpost.Application.Common;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Yaml;

namespace Dynpost.Infrastructure.Tests;

public class YamlRequestFileFormatTests
{
    private readonly YamlRequestFileFormat _format = new();

    [Fact]
    public void Parse_ReadsARequest_NamedAfterTheFileWhenUnnamed()
    {
        var result = _format.Parse("method: POST\nurl: https://example.com\n", WorkspacePath.Parse("users/create-user.yaml"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("create-user", result.Value.Name);
        Assert.Equal("https://example.com", result.Value.Url);
    }

    [Fact]
    public void Parse_InvalidText_IsAnInvalidFile()
    {
        var result = _format.Parse("url: [\n", WorkspacePath.Parse("broken.yaml"));

        Assert.Equal(ErrorCodes.InvalidFile, result.Error?.Code);
        Assert.Contains("broken.yaml", result.Error!.Message);
    }

    [Fact]
    public void Format_WritesWhatParseReads()
    {
        var path = WorkspacePath.Parse("requests/get-user.yaml");
        var parsed = _format.Parse("name: Get user\nmethod: GET\nurl: https://example.com/users/1\n", path).Value;

        var text = _format.Format(parsed);

        Assert.Contains("url: https://example.com/users/1", text);
        Assert.EndsWith("\n", text);
        Assert.Equal(parsed.Url, _format.Parse(text, path).Value.Url);
    }
}
