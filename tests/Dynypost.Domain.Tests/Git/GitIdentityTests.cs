using Dynypost.Domain.Git;

namespace Dynypost.Domain.Tests.Git;

public class GitIdentityTests
{
    [Theory]
    [InlineData(null, "ada@example.com")]
    [InlineData("  ", "ada@example.com")]
    [InlineData("Ada", null)]
    [InlineData("Ada", "ada.example.com")]
    [InlineData("Ada", "<ada@example.com>")]
    [InlineData("Ada\tL", "ada@example.com")]
    public void TryCreate_RejectsIncompleteOrUnsafeValues(string? name, string? email)
    {
        Assert.False(GitIdentity.TryCreate(name, email, out var identity));
        Assert.Null(identity);
    }

    [Fact]
    public void TryCreate_TrimsBothParts()
    {
        Assert.True(GitIdentity.TryCreate("  Ada Lovelace ", " ada@example.com\n", out var identity));

        Assert.Equal("Ada Lovelace", identity.Name);
        Assert.Equal("ada@example.com", identity.Email);
    }
}
