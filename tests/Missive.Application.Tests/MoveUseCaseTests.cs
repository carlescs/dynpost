using Missive.Application.Common;
using Missive.Application.Folders;
using Missive.Application.Requests;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests;

public class MoveUseCaseTests
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly CurrentWorkspace _current = new();

    public MoveUseCaseTests()
    {
        _current.Open(Root);
        _repository.MoveFile(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.MoveFolder(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        foreach (var folder in new[] { "users", "users/admin", "orders" })
        {
            _repository.DirectoryExists(Root, P(folder)).Returns(true);
        }
    }

    [Fact]
    public void MoveRequest_MovesTheFileKeepingItsName()
    {
        _repository.FileExists(Root, P("users/get-user.yaml")).Returns(true);

        var result = new MoveRequest(_repository, _current).Execute("users/get-user.yaml", "orders");

        Assert.Equal("orders/get-user.yaml", result.Value.Path);
        _repository.Received(1).MoveFile(Root, P("users/get-user.yaml"), P("orders/get-user.yaml"));
    }

    [Fact]
    public void MoveRequest_ToTheWorkspaceRoot()
    {
        _repository.FileExists(Root, P("users/get-user.yaml")).Returns(true);

        var result = new MoveRequest(_repository, _current).Execute("users/get-user.yaml", "");

        Assert.Equal("get-user.yaml", result.Value.Path);
    }

    [Fact]
    public void MoveRequest_ToItsOwnFolderDoesNothing()
    {
        _repository.FileExists(Root, P("users/get-user.yaml")).Returns(true);

        var result = new MoveRequest(_repository, _current).Execute("users/get-user.yaml", "users");

        Assert.Equal("users/get-user.yaml", result.Value.Path);
        _repository.DidNotReceiveWithAnyArgs().MoveFile(default!, default!, default!);
    }

    [Fact]
    public void MoveRequest_RefusesToOverwrite()
    {
        _repository.FileExists(Root, P("users/get-user.yaml")).Returns(true);
        _repository.EntryExists(Root, P("orders/get-user.yaml")).Returns(true);

        var result = new MoveRequest(_repository, _current).Execute("users/get-user.yaml", "orders");

        Assert.Equal(ErrorCodes.AlreadyExists, result.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().MoveFile(default!, default!, default!);
    }

    [Theory]
    [InlineData("users/get-user.yaml", "missing", ErrorCodes.NotFound)]
    [InlineData("users/get-user.yaml", "environments", ErrorCodes.InvalidPath)]
    [InlineData("users/get-user.yaml", "../elsewhere", ErrorCodes.InvalidPath)]
    [InlineData("users/notes.md", "orders", ErrorCodes.InvalidPath)]
    [InlineData("users/missing.yaml", "orders", ErrorCodes.NotFound)]
    [InlineData("users/missive.yaml", "", ErrorCodes.InvalidPath)]
    public void MoveRequest_RejectsInvalidMoves(string path, string folder, string code)
    {
        _repository.FileExists(Root, P("users/get-user.yaml")).Returns(true);
        _repository.FileExists(Root, P("users/missive.yaml")).Returns(true);
        _repository.FileExists(Root, P("users/notes.md")).Returns(true);

        var result = new MoveRequest(_repository, _current).Execute(path, folder);

        Assert.Equal(code, result.Error?.Code);
    }

    [Fact]
    public void MoveFolder_MovesItIntoAnotherFolder()
    {
        var result = new MoveFolder(_repository, _current).Execute("users/admin", "orders");

        Assert.Equal("orders/admin", result.Value.Path);
        _repository.Received(1).MoveFolder(Root, P("users/admin"), P("orders/admin"));
    }

    [Fact]
    public void MoveFolder_ToTheRoot()
    {
        var result = new MoveFolder(_repository, _current).Execute("users/admin", "");

        Assert.Equal("admin", result.Value.Path);
    }

    [Fact]
    public void MoveFolder_ToItsCurrentParentDoesNothing()
    {
        var result = new MoveFolder(_repository, _current).Execute("users/admin", "users");

        Assert.Equal("users/admin", result.Value.Path);
        _repository.DidNotReceiveWithAnyArgs().MoveFolder(default!, default!, default!);
    }

    [Theory]
    [InlineData("users", "users")]
    [InlineData("users", "users/admin")]
    public void MoveFolder_RefusesToMoveIntoItself(string path, string parent)
    {
        var result = new MoveFolder(_repository, _current).Execute(path, parent);

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().MoveFolder(default!, default!, default!);
    }

    [Fact]
    public void MoveFolder_AllowsSiblingsWithACommonPrefix()
    {
        _repository.DirectoryExists(Root, P("users-old")).Returns(true);

        var result = new MoveFolder(_repository, _current).Execute("users", "users-old");

        Assert.Equal("users-old/users", result.Value.Path);
    }

    [Fact]
    public void MoveFolder_RefusesToOverwriteOrUseReservedNames()
    {
        _repository.EntryExists(Root, P("orders/admin")).Returns(true);
        var clash = new MoveFolder(_repository, _current).Execute("users/admin", "orders");
        Assert.Equal(ErrorCodes.AlreadyExists, clash.Error?.Code);

        _repository.DirectoryExists(Root, P("users/environments")).Returns(true);
        var reserved = new MoveFolder(_repository, _current).Execute("users/environments", "");
        Assert.Equal(ErrorCodes.InvalidPath, reserved.Error?.Code);
    }

    [Fact]
    public void MoveFolder_RejectsTheRootAndMissingFolders()
    {
        Assert.Equal(ErrorCodes.InvalidPath, new MoveFolder(_repository, _current).Execute("", "users").Error?.Code);
        Assert.Equal(ErrorCodes.NotFound, new MoveFolder(_repository, _current).Execute("missing", "users").Error?.Code);
        Assert.Equal(ErrorCodes.NotFound, new MoveFolder(_repository, _current).Execute("users", "missing").Error?.Code);
    }

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);
}
