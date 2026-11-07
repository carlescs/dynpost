using Missive.Application.Common;
using Missive.Application.Folders;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests;

public class FolderUseCaseTests
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly CurrentWorkspace _current = new();

    public FolderUseCaseTests()
    {
        _current.Open(Root);
        _repository.CreateFolder(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.MoveFolder(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.DeleteFolder(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
    }

    [Fact]
    public void Create_CreatesATrimmedFolderInsideTheParent()
    {
        _repository.DirectoryExists(Root, P("users")).Returns(true);

        var result = new CreateFolder(_repository, _current).Execute("users", "  admin ");

        Assert.Equal("users/admin", result.Value.Path);
        _repository.Received(1).CreateFolder(Root, P("users/admin"));
    }

    [Fact]
    public void Create_AtTheRoot()
    {
        var result = new CreateFolder(_repository, _current).Execute("", "orders");

        Assert.Equal("orders", result.Value.Path);
    }

    [Theory]
    [InlineData("", "a/b", ErrorCodes.InvalidPath)]
    [InlineData("", "environments", ErrorCodes.InvalidPath)]
    [InlineData("../outside", "x", ErrorCodes.InvalidPath)]
    [InlineData(".git", "x", ErrorCodes.InvalidPath)]
    [InlineData("missing", "x", ErrorCodes.NotFound)]
    public void Create_RejectsInvalidInput(string parent, string name, string code)
    {
        var result = new CreateFolder(_repository, _current).Execute(parent, name);

        Assert.Equal(code, result.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().CreateFolder(default!, default!);
    }

    [Fact]
    public void Create_RefusesExistingNames()
    {
        _repository.EntryExists(Root, P("orders")).Returns(true);

        var result = new CreateFolder(_repository, _current).Execute("", "orders");

        Assert.Equal(ErrorCodes.AlreadyExists, result.Error?.Code);
    }

    [Fact]
    public void Rename_MovesTheFolderWithinItsParent()
    {
        _repository.DirectoryExists(Root, P("api/users")).Returns(true);

        var result = new RenameFolder(_repository, _current).Execute("api/users", "people");

        Assert.Equal("api/people", result.Value.Path);
        _repository.Received(1).MoveFolder(Root, P("api/users"), P("api/people"));
    }

    [Fact]
    public void Rename_RefusesExistingNamesButAllowsCaseChanges()
    {
        _repository.DirectoryExists(Root, P("users")).Returns(true);
        _repository.EntryExists(Root, Arg.Any<WorkspacePath>()).Returns(true);

        var clash = new RenameFolder(_repository, _current).Execute("users", "orders");
        Assert.Equal(ErrorCodes.AlreadyExists, clash.Error?.Code);

        var caseOnly = new RenameFolder(_repository, _current).Execute("users", "Users");
        Assert.Equal("Users", caseOnly.Value.Path);
        _repository.Received(1).MoveFolder(Root, P("users"), P("Users"));
    }

    [Fact]
    public void Rename_ToTheSameNameDoesNothing()
    {
        _repository.DirectoryExists(Root, P("users")).Returns(true);

        var result = new RenameFolder(_repository, _current).Execute("users", "users");

        Assert.Equal("users", result.Value.Path);
        _repository.DidNotReceiveWithAnyArgs().MoveFolder(default!, default!, default!);
    }

    [Fact]
    public void Rename_RejectsTheWorkspaceRoot()
    {
        var result = new RenameFolder(_repository, _current).Execute("", "x");

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
    }

    [Fact]
    public void Delete_RemovesFoldersThatOnlyHoldRequests()
    {
        GivenFolder("users", files: ["users/get-user.yaml", "users/_folder.yaml", "users/admin/.gitkeep"], folders: ["users/admin"]);

        var result = new DeleteFolder(_repository, _current).Execute("users");

        Assert.True(result.IsSuccess);
        _repository.Received(1).DeleteFolder(Root, P("users"));
    }

    [Fact]
    public void Delete_RefusesFoldersWithOtherFiles()
    {
        GivenFolder("users", files: ["users/get-user.yaml", "users/notes.md", "users/admin/run.sh"], folders: ["users/admin"]);

        var result = new DeleteFolder(_repository, _current).Execute("users");

        Assert.Equal(ErrorCodes.FolderNotEmpty, result.Error?.Code);
        Assert.Contains("users/admin/run.sh, users/notes.md", result.Error!.Message);
        _repository.DidNotReceiveWithAnyArgs().DeleteFolder(default!, default!);
    }

    [Fact]
    public void Delete_RefusesFoldersWithLinks()
    {
        GivenFolder("users", files: [], folders: [], links: ["users/shared"]);

        var result = new DeleteFolder(_repository, _current).Execute("users");

        Assert.Equal(ErrorCodes.FolderNotEmpty, result.Error?.Code);
    }

    [Fact]
    public void Delete_RefusesFoldersWithUnsupportedNames()
    {
        GivenFolder(
            "users",
            files: ["users/get-user.yaml"],
            folders: [],
            unsupported: ["notes:2026.txt", "admin/back\\slash.txt", "drafts:old"]);

        var result = new DeleteFolder(_repository, _current).Execute("users");

        Assert.Equal(ErrorCodes.FolderNotEmpty, result.Error?.Code);
        Assert.Contains(
            "users/admin/back\\slash.txt, users/drafts:old, users/notes:2026.txt",
            result.Error!.Message);
        _repository.DidNotReceiveWithAnyArgs().DeleteFolder(default!, default!);
    }

    [Fact]
    public void Delete_ListsUnsupportedNamesWithOtherBlockingFiles()
    {
        GivenFolder(
            "users",
            files: ["users/notes.md", "users/run.sh"],
            folders: [],
            links: ["users/shared"],
            unsupported: ["a:b.txt"]);

        var result = new DeleteFolder(_repository, _current).Execute("users");

        Assert.Equal(ErrorCodes.FolderNotEmpty, result.Error?.Code);
        Assert.Contains("(users/a:b.txt, users/notes.md, users/run.sh and 1 more)", result.Error!.Message);
        _repository.DidNotReceiveWithAnyArgs().DeleteFolder(default!, default!);
    }

    [Fact]
    public void Delete_RequiresAnOpenWorkspace()
    {
        var result = new DeleteFolder(_repository, new CurrentWorkspace()).Execute("users");

        Assert.Equal(ErrorCodes.NoWorkspace, result.Error?.Code);
    }

    private void GivenFolder(
        string path, string[] files, string[] folders, string[]? links = null, string[]? unsupported = null)
    {
        _repository.DirectoryExists(Root, P(path)).Returns(true);
        _repository.ReadFolderContents(Root, P(path)).Returns(new FolderContents(
            files.Select(P).ToList(),
            folders.Select(P).ToList(),
            (links ?? []).Select(P).ToList(),
            unsupported ?? []));
    }

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);
}
