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
    [InlineData("requests", "users", true)]
    [InlineData("requests", "environments", true)]
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

    [Theory]
    [InlineData("requests/health.yaml", true)]
    [InlineData("requests/users/get-user.yml", true)]
    [InlineData("requests/users/admin/create-user.yaml", true)]
    [InlineData("requests/missive.yaml", true)]
    [InlineData("requests/environments/get.yaml", true)]
    [InlineData("requests/users/_folder.yaml", false)]
    [InlineData("requests/users/smoke.run.yaml", false)]
    [InlineData("requests/users/.hidden/get.yaml", false)]
    [InlineData("health.yaml", false)]
    [InlineData("users/get-user.yaml", false)]
    [InlineData("missive.yaml", false)]
    [InlineData("environments/dev.yaml", false)]
    [InlineData("plans/get.yaml", false)]
    [InlineData(".github/workflow.yaml", false)]
    [InlineData("requests/README.md", false)]
    public void IsRequestPath_AcceptsRequestFilesInTheRequestsFolderOnly(string path, bool expected)
    {
        Assert.Equal(expected, WorkspaceLayout.IsRequestPath(WorkspacePath.Parse(path)));
    }

    [Theory]
    [InlineData("plans/smoke.run.yaml", true)]
    [InlineData("plans/checkout/smoke.run.yml", true)]
    [InlineData("plans/.hidden/smoke.run.yaml", false)]
    [InlineData("plans/get.yaml", false)]
    [InlineData("smoke.run.yaml", false)]
    [InlineData("requests/smoke.run.yaml", false)]
    public void IsRunPlanPath_AcceptsRunPlansInThePlansFolderOnly(string path, bool expected)
    {
        Assert.Equal(expected, WorkspaceLayout.IsRunPlanPath(WorkspacePath.Parse(path)));
    }

    [Theory]
    [InlineData("requests", true)]
    [InlineData("requests/users", true)]
    [InlineData("requests/users/.hidden", false)]
    [InlineData("requestsx", false)]
    [InlineData("", false)]
    [InlineData("plans", false)]
    public void IsAreaFolder_AcceptsTheAreaAndVisibleFoldersInIt(string folder, bool expected)
    {
        Assert.Equal(expected, WorkspaceLayout.IsAreaFolder(WorkspaceLayout.RequestsRoot, WorkspacePath.Parse(folder)));
    }

    [Theory]
    [InlineData("Users", "Users")]
    [InlineData("  Shop: API / v2 ", "Shop- API - v2")]
    [InlineData("...hidden.", "hidden")]
    [InlineData("CON", "CON_")]
    [InlineData("line\nbreak", "line break")]
    [InlineData("", "Folder")]
    [InlineData("???", "---")]
    public void ToFolderName_MakesAValidFolderName(string name, string expected)
    {
        var folder = WorkspaceLayout.ToFolderName(name);

        Assert.Equal(expected, folder);
        Assert.Null(WorkspaceLayout.ValidateFolderName(WorkspacePath.Parse("users"), folder));
    }

    [Fact]
    public void ToFolderName_ShortensLongNames()
    {
        var folder = WorkspaceLayout.ToFolderName(new string('a', 300));

        Assert.Equal(100, folder.Length);
    }

    [Theory]
    [InlineData("checkout-smoke.run.yaml", ".run.yaml", "checkout-smoke")]
    [InlineData("smoke.RUN.yml", ".RUN.yml", "smoke")]
    [InlineData("smoke.yaml", null, "smoke")]
    public void RunPlanExtensionAndStem(string fileName, string? extension, string stem)
    {
        Assert.Equal(extension, WorkspaceLayout.RunPlanExtension(fileName));
        Assert.Equal(stem, WorkspaceLayout.RunPlanStem(fileName));
    }

    [Fact]
    public void RunPlanFileName_SlugsTheNameAndCountsAttempts()
    {
        Assert.Equal("checkout-smoke.run.yaml", WorkspaceLayout.RunPlanFileName("Checkout smoke"));
        Assert.Equal("checkout-smoke-2.run.yml", WorkspaceLayout.RunPlanFileName("Checkout smoke", 2, ".run.yml"));
        Assert.Equal("plan.run.yaml", WorkspaceLayout.RunPlanFileName("!!"));
    }
}
