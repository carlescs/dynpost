using Dynypost.Application.Common;
using Dynypost.Application.History;
using Dynypost.Application.Sharing;
using Dynypost.Application.Folders;
using Dynypost.Application.Requests;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Workspaces;
using NSubstitute;

namespace Dynypost.Application.Tests;

public class MoveUseCaseTests
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
    private readonly IHistoryStore _history = Substitute.For<IHistoryStore>();
    private readonly CurrentWorkspace _current = new();

    public MoveUseCaseTests()
    {
        _current.Open(Root);
        _repository.MoveFile(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.MoveFolder(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        foreach (var folder in new[] { "requests/users", "requests/users/admin", "requests/orders" })
        {
            _repository.DirectoryExists(Root, P(folder)).Returns(true);
        }
    }

    [Fact]
    public void MoveRequest_MovesTheFileKeepingItsName()
    {
        _repository.FileExists(Root, P("requests/users/get-user.yaml")).Returns(true);

        var result = new MoveRequest(_repository, _localFiles, _history, _current).Execute("requests/users/get-user.yaml", "requests/orders");

        Assert.Equal("requests/orders/get-user.yaml", result.Value.Path);
        _repository.Received(1).MoveFile(Root, P("requests/users/get-user.yaml"), P("requests/orders/get-user.yaml"));
    }

    [Fact]
    public void MoveRequest_ToTheWorkspaceRoot()
    {
        _repository.FileExists(Root, P("requests/users/get-user.yaml")).Returns(true);

        var result = new MoveRequest(_repository, _localFiles, _history, _current).Execute("requests/users/get-user.yaml", "requests");

        Assert.Equal("requests/get-user.yaml", result.Value.Path);
    }

    [Fact]
    public void MoveRequest_ToItsOwnFolderDoesNothing()
    {
        _repository.FileExists(Root, P("requests/users/get-user.yaml")).Returns(true);

        var result = new MoveRequest(_repository, _localFiles, _history, _current).Execute("requests/users/get-user.yaml", "requests/users");

        Assert.Equal("requests/users/get-user.yaml", result.Value.Path);
        _repository.DidNotReceiveWithAnyArgs().MoveFile(default!, default!, default!);
    }

    [Fact]
    public void MoveRequest_RefusesToOverwrite()
    {
        _repository.FileExists(Root, P("requests/users/get-user.yaml")).Returns(true);
        _repository.EntryExists(Root, P("requests/orders/get-user.yaml")).Returns(true);

        var result = new MoveRequest(_repository, _localFiles, _history, _current).Execute("requests/users/get-user.yaml", "requests/orders");

        Assert.Equal(ErrorCodes.AlreadyExists, result.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().MoveFile(default!, default!, default!);
    }

    [Theory]
    [InlineData("requests/users/get-user.yaml", "requests/missing", ErrorCodes.NotFound)]
    [InlineData("requests/users/get-user.yaml", "environments", ErrorCodes.InvalidPath)]
    [InlineData("requests/users/get-user.yaml", "", ErrorCodes.InvalidPath)]
    [InlineData("requests/users/get-user.yaml", "plans", ErrorCodes.InvalidPath)]
    [InlineData("users/get-user.yaml", "requests/orders", ErrorCodes.InvalidPath)]
    [InlineData("requests/users/get-user.yaml", "../elsewhere", ErrorCodes.InvalidPath)]
    [InlineData("requests/users/notes.md", "requests/orders", ErrorCodes.InvalidPath)]
    [InlineData("requests/users/missing.yaml", "requests/orders", ErrorCodes.NotFound)]
    public void MoveRequest_RejectsInvalidMoves(string path, string folder, string code)
    {
        _repository.FileExists(Root, P("requests/users/get-user.yaml")).Returns(true);
        _repository.FileExists(Root, P("requests/users/dynypost.yaml")).Returns(true);
        _repository.FileExists(Root, P("requests/users/notes.md")).Returns(true);

        var result = new MoveRequest(_repository, _localFiles, _history, _current).Execute(path, folder);

        Assert.Equal(code, result.Error?.Code);
    }

    [Fact]
    public void MoveFolder_MovesItIntoAnotherFolder()
    {
        var result = new MoveFolder(_repository, _localFiles, _history, _current).Execute("requests/users/admin", "requests/orders");

        Assert.Equal("requests/orders/admin", result.Value.Path);
        _repository.Received(1).MoveFolder(Root, P("requests/users/admin"), P("requests/orders/admin"));
    }

    [Fact]
    public void MoveFolder_ToTheRoot()
    {
        var result = new MoveFolder(_repository, _localFiles, _history, _current).Execute("requests/users/admin", "requests");

        Assert.Equal("requests/admin", result.Value.Path);
    }

    [Fact]
    public void MoveFolder_ToItsCurrentParentDoesNothing()
    {
        var result = new MoveFolder(_repository, _localFiles, _history, _current).Execute("requests/users/admin", "requests/users");

        Assert.Equal("requests/users/admin", result.Value.Path);
        _repository.DidNotReceiveWithAnyArgs().MoveFolder(default!, default!, default!);
    }

    [Theory]
    [InlineData("requests/users", "requests/users")]
    [InlineData("requests/users", "requests/users/admin")]
    public void MoveFolder_RefusesToMoveIntoItself(string path, string parent)
    {
        var result = new MoveFolder(_repository, _localFiles, _history, _current).Execute(path, parent);

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().MoveFolder(default!, default!, default!);
    }

    [Fact]
    public void MoveFolder_AllowsSiblingsWithACommonPrefix()
    {
        _repository.DirectoryExists(Root, P("requests/users-old")).Returns(true);

        var result = new MoveFolder(_repository, _localFiles, _history, _current).Execute("requests/users", "requests/users-old");

        Assert.Equal("requests/users-old/users", result.Value.Path);
    }

    [Fact]
    public void MoveFolder_RefusesToOverwriteOrUseReservedNames()
    {
        _repository.EntryExists(Root, P("requests/orders/admin")).Returns(true);
        var clash = new MoveFolder(_repository, _localFiles, _history, _current).Execute("requests/users/admin", "requests/orders");
        Assert.Equal(ErrorCodes.AlreadyExists, clash.Error?.Code);

        var outside = new MoveFolder(_repository, _localFiles, _history, _current).Execute("requests/users/admin", "");
        Assert.Equal(ErrorCodes.InvalidPath, outside.Error?.Code);
    }

    [Fact]
    public void MoveFolder_RejectsTheRootAndMissingFolders()
    {
        Assert.Equal(ErrorCodes.InvalidPath, new MoveFolder(_repository, _localFiles, _history, _current).Execute("", "requests/users").Error?.Code);
        Assert.Equal(ErrorCodes.NotFound, new MoveFolder(_repository, _localFiles, _history, _current).Execute("requests/missing", "requests/users").Error?.Code);
        Assert.Equal(ErrorCodes.NotFound, new MoveFolder(_repository, _localFiles, _history, _current).Execute("requests/users", "requests/missing").Error?.Code);
    }

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);

    [Fact]
    public void MoveRequest_KeepsALocalOnlyRequestLocal()
    {
        _repository.FileExists(Root, P("requests/users/get-user.yaml")).Returns(true);
        _localFiles.Move(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);

        new MoveRequest(_repository, _localFiles, _history, _current).Execute("requests/users/get-user.yaml", "requests/orders");

        _localFiles.Received(1).Move(Root, P("requests/users/get-user.yaml"), P("requests/orders/get-user.yaml"));
    }

    [Fact]
    public void MoveRequest_TakesItsHistoryAlong()
    {
        _repository.FileExists(Root, P("requests/users/get-user.yaml")).Returns(true);
        _localFiles.Move(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);

        new MoveRequest(_repository, _localFiles, _history, _current).Execute("requests/users/get-user.yaml", "requests/orders");

        _history.Received(1).Move(Root, P("requests/users/get-user.yaml"), P("requests/orders/get-user.yaml"));
    }

    [Fact]
    public void MoveFolder_TakesTheHistoryOfItsRequestsAlong()
    {
        _localFiles.Move(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);

        new MoveFolder(_repository, _localFiles, _history, _current).Execute("requests/users/admin", "requests/orders");

        _history.Received(1).Move(Root, P("requests/users/admin"), P("requests/orders/admin"));
    }

    [Fact]
    public void MoveFolder_KeepsLocalOnlyRequestsInsideLocal()
    {
        _localFiles.Move(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);

        new MoveFolder(_repository, _localFiles, _history, _current).Execute("requests/users/admin", "requests/orders");

        _localFiles.Received(1).Move(Root, P("requests/users/admin"), P("requests/orders/admin"));
    }

    [Fact]
    public void MoveRequest_ReportsWhenItCannotKeepTheRequestLocal()
    {
        _repository.FileExists(Root, P("requests/users/get-user.yaml")).Returns(true);
        _localFiles.Move(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>())
            .Returns(new Error(ErrorCodes.Io, "read-only"));

        var result = new MoveRequest(_repository, _localFiles, _history, _current).Execute("requests/users/get-user.yaml", "requests/orders");

        Assert.Equal(ErrorCodes.Io, result.Error?.Code);
    }
}
