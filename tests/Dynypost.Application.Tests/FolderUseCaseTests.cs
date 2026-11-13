using Dynypost.Application.Common;
using Dynypost.Application.History;
using Dynypost.Application.Sharing;
using Dynypost.Application.Folders;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Workspaces;
using NSubstitute;

namespace Dynypost.Application.Tests;

public class FolderUseCaseTests
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
    private readonly IHistoryStore _history = Substitute.For<IHistoryStore>();
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
        _repository.DirectoryExists(Root, P("requests/users")).Returns(true);

        var result = new CreateFolder(_repository, _current).Execute("requests/users", "  admin ");

        Assert.Equal("requests/users/admin", result.Value.Path);
        _repository.Received(1).CreateFolder(Root, P("requests/users/admin"));
    }

    [Fact]
    public void Create_AtTheRoot()
    {
        var result = new CreateFolder(_repository, _current).Execute("requests", "orders");

        Assert.Equal("requests/orders", result.Value.Path);
    }

    [Theory]
    [InlineData("requests", "a/b", ErrorCodes.InvalidPath)]
    [InlineData("", "x", ErrorCodes.InvalidPath)]
    [InlineData("environments", "x", ErrorCodes.InvalidPath)]
    [InlineData("../outside", "x", ErrorCodes.InvalidPath)]
    [InlineData(".git", "x", ErrorCodes.InvalidPath)]
    [InlineData("requests/missing", "x", ErrorCodes.NotFound)]
    public void Create_RejectsInvalidInput(string parent, string name, string code)
    {
        var result = new CreateFolder(_repository, _current).Execute(parent, name);

        Assert.Equal(code, result.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().CreateFolder(default!, default!);
    }

    [Fact]
    public void Create_InThePlansFolder()
    {
        var result = new CreateFolder(_repository, _current).Execute("plans", "perf");

        Assert.Equal("plans/perf", result.Value.Path);
    }

    [Fact]
    public void Create_RefusesExistingNames()
    {
        _repository.EntryExists(Root, P("requests/orders")).Returns(true);

        var result = new CreateFolder(_repository, _current).Execute("requests", "orders");

        Assert.Equal(ErrorCodes.AlreadyExists, result.Error?.Code);
    }

    [Fact]
    public void Rename_MovesTheFolderWithinItsParent()
    {
        _repository.DirectoryExists(Root, P("requests/api/users")).Returns(true);

        var result = new RenameFolder(_repository, _localFiles, _history, _current).Execute("requests/api/users", "people");

        Assert.Equal("requests/api/people", result.Value.Path);
        _repository.Received(1).MoveFolder(Root, P("requests/api/users"), P("requests/api/people"));
        _history.Received(1).Move(Root, P("requests/api/users"), P("requests/api/people"));
    }

    [Fact]
    public void Rename_RefusesExistingNamesButAllowsCaseChanges()
    {
        _repository.DirectoryExists(Root, P("requests/users")).Returns(true);
        _repository.EntryExists(Root, Arg.Any<WorkspacePath>()).Returns(true);

        var clash = new RenameFolder(_repository, _localFiles, _history, _current).Execute("requests/users", "orders");
        Assert.Equal(ErrorCodes.AlreadyExists, clash.Error?.Code);

        var caseOnly = new RenameFolder(_repository, _localFiles, _history, _current).Execute("requests/users", "Users");
        Assert.Equal("requests/Users", caseOnly.Value.Path);
        _repository.Received(1).MoveFolder(Root, P("requests/users"), P("requests/Users"));
    }

    [Fact]
    public void Rename_ToTheSameNameDoesNothing()
    {
        _repository.DirectoryExists(Root, P("requests/users")).Returns(true);

        var result = new RenameFolder(_repository, _localFiles, _history, _current).Execute("requests/users", "users");

        Assert.Equal("requests/users", result.Value.Path);
        _repository.DidNotReceiveWithAnyArgs().MoveFolder(default!, default!, default!);
    }

    [Fact]
    public void Rename_RejectsTheWorkspaceRoot()
    {
        var result = new RenameFolder(_repository, _localFiles, _history, _current).Execute("", "x");

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
    }

    [Fact]
    public void Delete_RemovesFoldersThatOnlyHoldRequests()
    {
        GivenFolder("requests/users", files: ["requests/users/get-user.yaml", "requests/users/_folder.yaml", "requests/users/admin/.gitkeep"], folders: ["requests/users/admin"]);

        var result = new DeleteFolder(_repository, _localFiles, _history, _current).Execute("requests/users");

        Assert.True(result.IsSuccess);
        _repository.Received(1).DeleteFolder(Root, P("requests/users"));
        _history.Received(1).Remove(Root, P("requests/users"));
    }

    [Fact]
    public void Delete_RefusesFoldersWithOtherFiles()
    {
        GivenFolder("requests/users", files: ["requests/users/get-user.yaml", "requests/users/notes.md", "requests/users/admin/run.sh"], folders: ["requests/users/admin"]);

        var result = new DeleteFolder(_repository, _localFiles, _history, _current).Execute("requests/users");

        Assert.Equal(ErrorCodes.FolderNotEmpty, result.Error?.Code);
        Assert.Contains("requests/users/admin/run.sh, requests/users/notes.md", result.Error!.Message);
        _repository.DidNotReceiveWithAnyArgs().DeleteFolder(default!, default!);
    }

    [Fact]
    public void Delete_RefusesFoldersWithLinks()
    {
        GivenFolder("requests/users", files: [], folders: [], links: ["requests/users/shared"]);

        var result = new DeleteFolder(_repository, _localFiles, _history, _current).Execute("requests/users");

        Assert.Equal(ErrorCodes.FolderNotEmpty, result.Error?.Code);
    }

    [Fact]
    public void Delete_RefusesFoldersWithUnsupportedNames()
    {
        GivenFolder(
            "requests/users",
            files: ["requests/users/get-user.yaml"],
            folders: [],
            unsupported: ["notes:2026.txt", "admin/back\\slash.txt", "drafts:old"]);

        var result = new DeleteFolder(_repository, _localFiles, _history, _current).Execute("requests/users");

        Assert.Equal(ErrorCodes.FolderNotEmpty, result.Error?.Code);
        Assert.Contains(
            "requests/users/admin/back\\slash.txt, requests/users/drafts:old, requests/users/notes:2026.txt",
            result.Error!.Message);
        _repository.DidNotReceiveWithAnyArgs().DeleteFolder(default!, default!);
    }

    [Fact]
    public void Delete_ListsUnsupportedNamesWithOtherBlockingFiles()
    {
        GivenFolder(
            "requests/users",
            files: ["requests/users/notes.md", "requests/users/run.sh"],
            folders: [],
            links: ["requests/users/shared"],
            unsupported: ["a:b.txt"]);

        var result = new DeleteFolder(_repository, _localFiles, _history, _current).Execute("requests/users");

        Assert.Equal(ErrorCodes.FolderNotEmpty, result.Error?.Code);
        Assert.Contains("(requests/users/a:b.txt, requests/users/notes.md, requests/users/run.sh and 1 more)", result.Error!.Message);
        _repository.DidNotReceiveWithAnyArgs().DeleteFolder(default!, default!);
    }

    [Fact]
    public void Delete_RequiresAnOpenWorkspace()
    {
        var result = new DeleteFolder(_repository, _localFiles, _history, new CurrentWorkspace()).Execute("requests/users");

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
