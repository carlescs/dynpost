using Dynpost.Domain.Git;

namespace Dynpost.Domain.Tests.Git;

public class GitStashTests
{
    [Fact]
    public void Ref_NamesTheEntryByItsIndex()
    {
        Assert.Equal("stash@{2}", new GitStash(2, "abc", "Work", "main", null).Ref);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \n ", null)]
    [InlineData("  Login form ", "Login form")]
    [InlineData("Login\r\n\r\n  form\n", "Login form")]
    public void NormalizeMessage_KeepsOneTrimmedLine(string? message, string? expected)
    {
        Assert.Equal(expected, GitStash.NormalizeMessage(message));
    }
}
