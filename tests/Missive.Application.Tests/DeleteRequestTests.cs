using Missive.Application.Common;
using Missive.Application.Requests;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests;

public class DeleteRequestTests
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly CurrentWorkspace _current = new();

    public DeleteRequestTests()
    {
        _current.Open(Root);
        _repository.DeleteFile(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
    }

    [Fact]
    public void DeletesTheFile()
    {
        var path = WorkspacePath.Parse("users/get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);

        var result = new DeleteRequest(_repository, _current).Execute("users/get-user.yaml");

        Assert.True(result.IsSuccess);
        _repository.Received(1).DeleteFile(Root, path);
    }

    [Fact]
    public void RefusesMissingFiles()
    {
        var result = new DeleteRequest(_repository, _current).Execute("users/missing.yaml");

        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().DeleteFile(default!, default!);
    }

    [Fact]
    public void RefusesNonRequestPaths()
    {
        var result = new DeleteRequest(_repository, _current).Execute("users/notes.txt");

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
    }
}
