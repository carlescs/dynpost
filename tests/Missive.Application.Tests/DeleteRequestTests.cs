using Missive.Application.Common;
using Missive.Application.History;
using Missive.Application.Sharing;
using Missive.Application.Requests;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests;

public class DeleteRequestTests
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
    private readonly IHistoryStore _history = Substitute.For<IHistoryStore>();
    private readonly CurrentWorkspace _current = new();

    public DeleteRequestTests()
    {
        _current.Open(Root);
        _repository.DeleteFile(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
    }

    [Fact]
    public void DeletesTheFile()
    {
        var path = WorkspacePath.Parse("requests/users/get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);

        var result = new DeleteRequest(_repository, _localFiles, _history, _current).Execute("requests/users/get-user.yaml");

        Assert.True(result.IsSuccess);
        _repository.Received(1).DeleteFile(Root, path);
    }

    [Fact]
    public void RefusesMissingFiles()
    {
        var result = new DeleteRequest(_repository, _localFiles, _history, _current).Execute("requests/users/missing.yaml");

        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().DeleteFile(default!, default!);
    }

    [Fact]
    public void RefusesNonRequestPaths()
    {
        var result = new DeleteRequest(_repository, _localFiles, _history, _current).Execute("requests/users/notes.txt");

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
    }

    [Fact]
    public void ForgetsTheFileIfItWasLocalOnly()
    {
        var path = WorkspacePath.Parse("requests/users/get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);

        new DeleteRequest(_repository, _localFiles, _history, _current).Execute("requests/users/get-user.yaml");

        _localFiles.Received(1).Remove(Root, path);
    }

    [Fact]
    public void DeletesTheRequestsHistory()
    {
        var path = WorkspacePath.Parse("requests/users/get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);

        new DeleteRequest(_repository, _localFiles, _history, _current).Execute("requests/users/get-user.yaml");

        _history.Received(1).Remove(Root, path);
    }
}
