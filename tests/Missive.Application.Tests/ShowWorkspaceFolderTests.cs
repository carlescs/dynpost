using Missive.Application.Common;
using Missive.Application.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests;

public class ShowWorkspaceFolderTests
{
    private const string Folder = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IFileManager _fileManager = Substitute.For<IFileManager>();
    private readonly CurrentWorkspace _current = new();

    private ShowWorkspaceFolder UseCase => new(_current, _repository, _fileManager);

    [Fact]
    public void Execute_ShowsTheOpenWorkspaceFolder()
    {
        _current.Open(Folder);
        _repository.FolderExists(Folder).Returns(true);
        _fileManager.ShowFolder(Folder).Returns(Unit.Value);

        var result = UseCase.Execute();

        Assert.True(result.IsSuccess);
        _fileManager.Received(1).ShowFolder(Folder);
    }

    [Fact]
    public void Execute_WithoutAWorkspace_Fails()
    {
        var result = UseCase.Execute();

        Assert.Equal(ErrorCodes.NoWorkspace, result.Error?.Code);
        _fileManager.DidNotReceiveWithAnyArgs().ShowFolder(default!);
    }

    [Fact]
    public void Execute_WhenTheFolderWasDeleted_FailsWithoutStartingTheFileManager()
    {
        _current.Open(Folder);
        _repository.FolderExists(Folder).Returns(false);

        var result = UseCase.Execute();

        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
        _fileManager.DidNotReceiveWithAnyArgs().ShowFolder(default!);
    }

    [Fact]
    public void Execute_ReportsAFileManagerThatCannotStart()
    {
        _current.Open(Folder);
        _repository.FolderExists(Folder).Returns(true);
        _fileManager.ShowFolder(Folder).Returns(new Error(ErrorCodes.Io, "No file manager."));

        var result = UseCase.Execute();

        Assert.Equal("No file manager.", result.Error?.Message);
    }
}
