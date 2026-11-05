using Missive.Domain.Git;

namespace Missive.Domain.Tests.Git;

public class ConflictMarkersTests
{
    [Theory]
    [InlineData("<<<<<<< HEAD\nname: Bob\n=======\nname: Alice\n>>>>>>> origin/main\n")]
    [InlineData("a\r\n<<<<<<< ours\r\nx\r\n=======\r\ny\r\n>>>>>>> theirs\r\nb\r\n")]
    [InlineData("<<<<<<< HEAD\n||||||| base\nold\n=======\nnew\n>>>>>>> branch\n")]
    public void FindsAConflictBlock(string text)
    {
        Assert.True(ConflictMarkers.Contains(text));
    }

    [Theory]
    [InlineData("name: Bob\n")]
    [InlineData("content: |\n  =======\n")]
    [InlineData("<<<<<<< HEAD\nonly a start\n")]
    [InlineData("  <<<<<<< indented\n=======\n>>>>>>> x\n")]
    public void IgnoresTextWithoutACompleteBlockAtLineStarts(string text)
    {
        Assert.False(ConflictMarkers.Contains(text));
    }

    [Fact]
    public void GitConflict_ChecksTheWorkingFile()
    {
        Assert.True(new GitConflict("a.yaml", "a", "b", "c", "<<<<<<< x\nb\n=======\nc\n>>>>>>> y\n").HasConflictMarkers);
        Assert.False(new GitConflict("a.yaml", "a", "b", "c", "b\n").HasConflictMarkers);
        Assert.False(new GitConflict("a.yaml", "a", "b", null, null).HasConflictMarkers);
    }
}
