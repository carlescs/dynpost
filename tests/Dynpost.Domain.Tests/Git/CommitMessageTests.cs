using Dynpost.Domain.Git;

namespace Dynpost.Domain.Tests.Git;

public class CommitMessageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t\n")]
    public void TryCreate_RejectsBlankText(string? text)
    {
        Assert.False(CommitMessage.TryCreate(text, out var message));
        Assert.Null(message);
    }

    [Fact]
    public void TryCreate_TrimsAndNormalizesLineEndings()
    {
        Assert.True(CommitMessage.TryCreate("  Add users\r\n\r\nWith details.\r\n", out var message));

        Assert.Equal("Add users\n\nWith details.", message.Value);
        Assert.Equal("Add users", message.Summary);
    }
}
