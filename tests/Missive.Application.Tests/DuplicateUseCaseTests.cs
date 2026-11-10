using Missive.Application.Common;
using Missive.Application.Sharing;
using Missive.Application.Folders;
using Missive.Application.Requests;
using Missive.Application.Workspaces;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests;

public class DuplicateUseCaseTests
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
    private readonly CurrentWorkspace _current = new();

    public DuplicateUseCaseTests()
    {
        _current.Open(Root);
        _repository.WriteRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _repository.CreateFolder(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.CopyFile(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _repository.DeleteFolder(Root, Arg.Any<WorkspacePath>()).Returns(Unit.Value);
    }

    [Fact]
    public async Task DuplicateRequest_WritesACopyNamedAfterTheOriginal()
    {
        var path = P("users/get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);
        _repository.ReadRequestAsync(Root, path, Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get user", RequestMethod.Get, "/users/1"));

        var result = await new DuplicateRequest(_repository, _localFiles, _current).ExecuteAsync("users/get-user.yaml");

        Assert.Equal("users/get-user-copy.yaml", result.Value.Path);
        Assert.Equal("Get user copy", result.Value.Request.Name);
        await _repository.Received(1).WriteRequestAsync(
            Root,
            P("users/get-user-copy.yaml"),
            Arg.Is<HttpRequestDefinition>(r => r.Name == "Get user copy" && r.Url == "/users/1"),
            Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().WriteRequestAsync(Root, path, Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateRequest_FollowsTheFileNameCollisionRules()
    {
        var path = P("get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);
        _repository.FileExists(Root, P("get-user-copy.yaml")).Returns(true);
        _repository.ReadRequestAsync(Root, path, Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get user", RequestMethod.Get, "/users/1"));

        var result = await new DuplicateRequest(_repository, _localFiles, _current).ExecuteAsync("get-user.yaml");

        Assert.Equal("get-user-copy-2.yaml", result.Value.Path);
    }

    [Fact]
    public async Task DuplicateRequest_ConcurrentCopiesGetTheirOwnFiles()
    {
        var path = P("get-user.yaml");
        var written = new HashSet<WorkspacePath>();
        _repository.FileExists(Root, Arg.Any<WorkspacePath>()).Returns(call =>
        {
            var candidate = call.ArgAt<WorkspacePath>(1);
            lock (written) return candidate == path || written.Contains(candidate);
        });
        _repository.ReadRequestAsync(Root, path, Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get user", RequestMethod.Get, "/users/1"));
        _repository.WriteRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(20);
                lock (written) written.Add(call.ArgAt<WorkspacePath>(1));
                return Result<bool>.Success(true);
            });

        var results = await Task.WhenAll(
            new DuplicateRequest(_repository, _localFiles, _current).ExecuteAsync("get-user.yaml"),
            new DuplicateRequest(_repository, _localFiles, _current).ExecuteAsync("get-user.yaml"));

        Assert.Equal(["get-user-copy-2.yaml", "get-user-copy.yaml"], results.Select(r => r.Value.Path).Order());
    }

    [Fact]
    public async Task DuplicateRequest_ReportsUnreadableFiles()
    {
        var path = P("broken.yaml");
        _repository.FileExists(Root, path).Returns(true);
        _repository.ReadRequestAsync(Root, path, Arg.Any<CancellationToken>())
            .Returns(Result<HttpRequestDefinition>.Failure(ErrorCodes.InvalidFile, "Bad YAML."));

        var result = await new DuplicateRequest(_repository, _localFiles, _current).ExecuteAsync("broken.yaml");

        Assert.Equal(ErrorCodes.InvalidFile, result.Error?.Code);
        await _repository.DidNotReceiveWithAnyArgs().WriteRequestAsync(default!, default!, default!, default);
    }

    [Theory]
    [InlineData("missive.yaml", ErrorCodes.InvalidPath)]
    [InlineData("environments/dev.yaml", ErrorCodes.InvalidPath)]
    [InlineData("missing.yaml", ErrorCodes.NotFound)]
    public async Task DuplicateRequest_RejectsInvalidPaths(string path, string code)
    {
        var result = await new DuplicateRequest(_repository, _localFiles, _current).ExecuteAsync(path);

        Assert.Equal(code, result.Error?.Code);
    }

    [Fact]
    public void DuplicateFolder_CopiesEverythingIntoANewFolder()
    {
        _repository.DirectoryExists(Root, P("api/users")).Returns(true);
        _repository.ReadFolderContents(Root, P("api/users")).Returns(new FolderContents(
            [P("api/users/get-user.yaml"), P("api/users/admin/list.yaml"), P("api/users/notes.md")],
            [P("api/users/admin"), P("api/users/admin/deep")],
            []));

        var result = new DuplicateFolder(_repository, _localFiles, _current).Execute("api/users");

        Assert.Equal("api/users copy", result.Value.Path);
        Received.InOrder(() =>
        {
            _repository.CreateFolder(Root, P("api/users copy"));
            _repository.CreateFolder(Root, P("api/users copy/admin"));
            _repository.CreateFolder(Root, P("api/users copy/admin/deep"));
        });
        _repository.Received(1).CopyFile(Root, P("api/users/get-user.yaml"), P("api/users copy/get-user.yaml"));
        _repository.Received(1).CopyFile(Root, P("api/users/admin/list.yaml"), P("api/users copy/admin/list.yaml"));
        _repository.Received(1).CopyFile(Root, P("api/users/notes.md"), P("api/users copy/notes.md"));
        _repository.Received(3).CopyFile(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>());
        _repository.DidNotReceiveWithAnyArgs().MoveFolder(default!, default!, default!);
    }

    [Theory]
    [InlineData("users/link", null)]
    [InlineData(null, "admin/a:b.txt")]
    public void DuplicateFolder_RefusesFoldersItCannotCopyCompletely(string? link, string? unsupported)
    {
        _repository.DirectoryExists(Root, P("users")).Returns(true);
        _repository.ReadFolderContents(Root, P("users")).Returns(new FolderContents(
            [P("users/a.yaml")],
            [],
            link is null ? [] : [P(link)],
            unsupported is null ? [] : [unsupported]));

        var result = new DuplicateFolder(_repository, _localFiles, _current).Execute("users");

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
        Assert.Contains(link ?? "users/" + unsupported, result.Error!.Message);
        _repository.DidNotReceiveWithAnyArgs().CreateFolder(default!, default!);
        _repository.DidNotReceiveWithAnyArgs().CopyFile(default!, default!, default!);
    }

    [Fact]
    public void DuplicateFolder_NumbersTheCopyWhenTheNameIsTaken()
    {
        _repository.DirectoryExists(Root, P("users")).Returns(true);
        _repository.EntryExists(Root, P("users copy")).Returns(true);
        _repository.ReadFolderContents(Root, P("users")).Returns(new FolderContents([], [], []));

        var result = new DuplicateFolder(_repository, _localFiles, _current).Execute("users");

        Assert.Equal("users copy 2", result.Value.Path);
        _repository.Received(1).CreateFolder(Root, P("users copy 2"));
    }

    [Fact]
    public void DuplicateFolder_RemovesAPartialCopyWhenCopyingFails()
    {
        _repository.DirectoryExists(Root, P("users")).Returns(true);
        _repository.ReadFolderContents(Root, P("users")).Returns(new FolderContents([P("users/a.yaml")], [], []));
        _repository.CopyFile(Root, P("users/a.yaml"), P("users copy/a.yaml"))
            .Returns(Result<Unit>.Failure(ErrorCodes.Io, "Disk full."));

        var result = new DuplicateFolder(_repository, _localFiles, _current).Execute("users");

        Assert.Equal(ErrorCodes.Io, result.Error?.Code);
        _repository.Received(1).DeleteFolder(Root, P("users copy"));
        _repository.DidNotReceive().DeleteFolder(Root, P("users"));
    }

    [Theory]
    [InlineData("", ErrorCodes.InvalidPath)]
    [InlineData("environments", ErrorCodes.InvalidPath)]
    [InlineData("missing", ErrorCodes.NotFound)]
    public void DuplicateFolder_RejectsInvalidPaths(string path, string code)
    {
        var result = new DuplicateFolder(_repository, _localFiles, _current).Execute(path);

        Assert.Equal(code, result.Error?.Code);
        _repository.DidNotReceiveWithAnyArgs().CreateFolder(default!, default!);
    }

    private static WorkspacePath P(string value) => WorkspacePath.Parse(value);

    [Fact]
    public async Task DuplicateRequest_TheCopyOfALocalOnlyRequestIsLocalOnly()
    {
        var path = P("users/get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);
        _repository.ReadRequestAsync(Root, path, Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get user", RequestMethod.Get, "/users/1"));
        _localFiles.Copy(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);

        await new DuplicateRequest(_repository, _localFiles, _current).ExecuteAsync("users/get-user.yaml");

        _localFiles.Received(1).Copy(Root, path, P("users/get-user-copy.yaml"));
    }

    [Fact]
    public void DuplicateFolder_CopiesOfLocalOnlyRequestsAreLocalOnly()
    {
        _repository.DirectoryExists(Root, P("users")).Returns(true);
        _repository.ReadFolderContents(Root, P("users")).Returns(new FolderContents([], [], []));
        _localFiles.Copy(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);

        new DuplicateFolder(_repository, _localFiles, _current).Execute("users");

        _localFiles.Received(1).Copy(Root, P("users"), P("users copy"));
    }
}
