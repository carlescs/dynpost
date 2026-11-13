using Dynypost.Domain.Workspaces;

namespace Dynypost.Domain.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Create user", "create-user")]
    [InlineData("  Get   user by ID ", "get-user-by-id")]
    [InlineData("Crème brûlée / v2", "creme-brulee-v2")]
    [InlineData("--already-slugged--", "already-slugged")]
    [InlineData("日本語", Slug.Fallback)]
    [InlineData("", Slug.Fallback)]
    [InlineData(null, Slug.Fallback)]
    public void From_ProducesLowercaseDashedAsciiSlug(string? name, string expected)
    {
        Assert.Equal(expected, Slug.From(name));
    }
}
