using Dynpost.Domain.Git;

namespace Dynpost.Domain.Tests.Git;

public class GitCredentialsTests
{
    [Theory]
    [InlineData("https://github.com", "https", "github.com")]
    [InlineData("https://me@github.com/team/api.git/", "https", "github.com")]
    [InlineData("http://git.local:8080/x", "http", "git.local:8080")]
    [InlineData("https://git.example.com:443/x", "https", "git.example.com")]
    public void ScopesTheCredentialsToTheHost(string url, string protocol, string host)
    {
        Assert.True(GitCredentials.TryCreate(url, " me ", "s3cret", out var credentials, out var error), error);
        Assert.Equal((protocol, host, "me", "s3cret"), (credentials.Protocol, credentials.Host, credentials.Username, credentials.Password));
        Assert.Equal($"{protocol}://{host}", credentials.Scope);
    }

    [Theory]
    [InlineData("git@github.com:team/api.git", "me", "pw")]
    [InlineData("ssh://git@github.com/team/api.git", "me", "pw")]
    [InlineData("not a url", "me", "pw")]
    [InlineData("https://github.com", " ", "pw")]
    [InlineData("https://github.com", "me", "")]
    [InlineData("https://github.com", "me", "pw\nprotocol=http")]
    [InlineData("https://github.com", "me\n", "pw")]
    public void RejectsWhatGitCantUse(string url, string username, string password)
    {
        Assert.False(GitCredentials.TryCreate(url, username, password, out var credentials, out var error));
        Assert.Null(credentials);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void ToString_HidesThePassword()
    {
        Assert.True(GitCredentials.TryCreate("https://github.com", "me", "s3cret", out var credentials, out _));

        Assert.DoesNotContain("s3cret", credentials.ToString());
    }
}
