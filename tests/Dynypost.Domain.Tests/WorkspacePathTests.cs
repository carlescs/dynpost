using Dynypost.Domain.Workspaces;

namespace Dynypost.Domain.Tests;

public class WorkspacePathTests
{
    [Theory]
    [InlineData("users/get-user.yaml", "users/get-user.yaml")]
    [InlineData("users\\get-user.yaml", "users/get-user.yaml")]
    [InlineData("users/", "users")]
    [InlineData("", "")]
    public void TryParse_NormalizesRelativePaths(string input, string expected)
    {
        Assert.True(WorkspacePath.TryParse(input, out var path));
        Assert.Equal(expected, path.Value);
    }

    [Theory]
    [InlineData("../secrets.yaml")]
    [InlineData("users/../../x.yaml")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/x.yaml")]
    [InlineData("users//x.yaml")]
    [InlineData("./x.yaml")]
    [InlineData(null)]
    public void TryParse_RejectsPathsThatCouldLeaveTheWorkspace(string? input)
    {
        Assert.False(WorkspacePath.TryParse(input, out _));
    }

    [Fact]
    public void ParentAndFileName_SplitTheLastSegment()
    {
        var path = WorkspacePath.Parse("users/admin/get-user.yaml");

        Assert.Equal("get-user.yaml", path.FileName);
        Assert.Equal("users/admin", path.Parent.Value);
        Assert.True(WorkspacePath.Parse("health.yaml").Parent.IsRoot);
    }

    [Fact]
    public void Combine_AppendsASegment()
    {
        Assert.Equal("users/x.yaml", WorkspacePath.Parse("users").Combine("x.yaml").Value);
        Assert.Equal("x.yaml", WorkspacePath.Root.Combine("x.yaml").Value);
        Assert.Throws<ArgumentException>(() => WorkspacePath.Root.Combine("../x.yaml"));
    }
}
