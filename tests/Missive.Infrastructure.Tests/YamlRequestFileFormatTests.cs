using Missive.Application.Common;
using Missive.Domain.Workspaces;
using Missive.Infrastructure.Yaml;

namespace Missive.Infrastructure.Tests;

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
}
