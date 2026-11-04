using Missive.Domain.Workspaces;

namespace Missive.Domain.Tests;

public class WorkspaceLayoutTests
{
    [Theory]
    [InlineData("", "get-user.yaml", true)]
    [InlineData("", "get-user.yml", true)]
    [InlineData("", "missive.yaml", false)]
    [InlineData("users", "missive.yaml", true)]
    [InlineData("users", "_folder.yaml", false)]
    [InlineData("", ".hidden.yaml", false)]
    [InlineData("", "notes.md", false)]
    public void IsRequestFile(string folder, string fileName, bool expected)
    {
        Assert.Equal(expected, WorkspaceLayout.IsRequestFile(WorkspacePath.Parse(folder), fileName));
    }

    [Theory]
    [InlineData("", "users", true)]
    [InlineData("", "environments", false)]
    [InlineData("users", "environments", true)]
    [InlineData("", ".git", false)]
    [InlineData("", ".missive", false)]
    public void IsCollectionFolder(string parent, string folderName, bool expected)
    {
        Assert.Equal(expected, WorkspaceLayout.IsCollectionFolder(WorkspacePath.Parse(parent), folderName));
    }

    [Fact]
    public void RequestFileName_AddsACounterAfterTheFirstAttempt()
    {
        Assert.Equal("create-user.yaml", WorkspaceLayout.RequestFileName("Create user"));
        Assert.Equal("create-user-2.yaml", WorkspaceLayout.RequestFileName("Create user", 2));
    }
}
