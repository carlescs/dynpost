using Dynpost.Domain.Git;

namespace Dynpost.Domain.Tests.Git;

public class RemoteTests
{
    [Theory]
    [InlineData("origin")]
    [InlineData("upstream")]
    [InlineData("team/backup-2")]
    [InlineData("my_remote.v2")]
    public void RemoteName_AcceptsGitRemoteNames(string text)
    {
        Assert.True(RemoteName.TryCreate(text, out var name, out var error), error);
        Assert.Equal(text, name.Value);
    }

    [Fact]
    public void RemoteName_IsTrimmed()
    {
        Assert.True(RemoteName.TryCreate("  origin ", out var name, out _));
        Assert.Equal("origin", name.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-x")]
    [InlineData(".hidden")]
    [InlineData("/abs")]
    [InlineData("a b")]
    [InlineData("a..b")]
    [InlineData("a//b")]
    [InlineData("a/.b")]
    [InlineData("a/")]
    [InlineData("a.")]
    [InlineData("a.lock")]
    [InlineData("a:b")]
    [InlineData("a~1")]
    [InlineData("a*")]
    [InlineData("caf\u00e9")]
    public void RemoteName_RejectsInvalidNames(string? text)
    {
        Assert.False(RemoteName.TryCreate(text, out var name, out var error));
        Assert.Null(name);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("https://github.com/acme/api.git")]
    [InlineData("git@github.com:acme/api.git")]
    [InlineData("ssh://git@example.com:2222/api.git")]
    [InlineData("../api.git")]
    [InlineData("C:\\repos\\api")]
    public void RemoteUrl_AcceptsUrlsAndPaths(string text)
    {
        Assert.True(RemoteUrl.TryCreate(text, out var url, out var error), error);
        Assert.Equal(text, url.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("--upload-pack=evil")]
    [InlineData("https://a\nhttps://b")]
    public void RemoteUrl_RejectsBlankMultilineOrOptionLikeText(string? text)
    {
        Assert.False(RemoteUrl.TryCreate(text, out var url, out var error));
        Assert.Null(url);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void LooksBinary_DetectsNulCharacters()
    {
        Assert.False(GitFileDiff.LooksBinary("name: A\n"));
        Assert.True(GitFileDiff.LooksBinary("PNG\0\u0001"));
    }
}
