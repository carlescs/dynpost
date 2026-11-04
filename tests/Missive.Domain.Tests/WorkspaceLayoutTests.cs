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

    [Theory]
    [InlineData("", "users")]
    [InlineData("", "Admin API v2")]
    [InlineData("users", "environments")]
    [InlineData("", "ünïcode")]
    public void ValidateFolderName_AcceptsPortableNames(string parent, string name)
    {
        Assert.Null(WorkspaceLayout.ValidateFolderName(WorkspacePath.Parse(parent), name));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("", "   ")]
    [InlineData("", " users")]
    [InlineData("", "a/b")]
    [InlineData("", "a\\b")]
    [InlineData("", "what?")]
    [InlineData("", "a:b")]
    [InlineData("", ".hidden")]
    [InlineData("", "..")]
    [InlineData("", "trailing.")]
    [InlineData("", "CON")]
    [InlineData("", "nul.txt")]
    [InlineData("", "environments")]
    [InlineData("", null)]
    public void ValidateFolderName_RejectsUnportableOrReservedNames(string parent, string? name)
    {
        Assert.NotNull(WorkspaceLayout.ValidateFolderName(WorkspacePath.Parse(parent), name));
    }

    [Theory]
    [InlineData("get-user.yaml", true)]
    [InlineData("old.yml", true)]
    [InlineData("_folder.yaml", true)]
    [InlineData(".gitkeep", true)]
    [InlineData(".DS_Store", true)]
    [InlineData("README.md", false)]
    [InlineData("script.sh", false)]
    [InlineData(".env", false)]
    public void IsDisposableFile(string fileName, bool expected)
    {
        Assert.Equal(expected, WorkspaceLayout.IsDisposableFile(fileName));
    }
}
