using Missive.Application.Common;
using Missive.Application.Workspaces;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;
using Missive.Infrastructure.Workspaces;

namespace Missive.Infrastructure.Tests;

public sealed class FileSystemWorkspaceRepositoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("missive-tests-").FullName;
    private readonly FileSystemWorkspaceRepository _repository = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task List_ReturnsCollectionsAndRequestsSortedFoldersFirst()
    {
        Write("health.yaml", "name: Health check\nmethod: GET\nurl: /health\n");
        Write("users/get-user.yaml", "name: Get user\nmethod: GET\nurl: /users/1\n");
        Write("users/_folder.yaml", "headers:\n  Accept: application/json\n");
        Write("users/broken.yaml", "name: [oops");
        Write("environments/dev.yaml", "name: Development\n");
        Write("missive.yaml", "name: my-api\n");
        Write(".missive/history.yaml", "x: 1\n");
        Write("README.md", "# hi\n");
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        var result = await _repository.ListAsync(_root);

        Assert.True(result.IsSuccess);
        Assert.Equal(["empty", "users", "health.yaml"], result.Value.Select(e => e.Path.Value));

        var users = Assert.IsType<FolderEntry>(result.Value[1]);
        Assert.Equal(["users/broken.yaml", "users/get-user.yaml"], users.Children.Select(e => e.Path.Value));
        var broken = Assert.IsType<RequestFileEntry>(users.Children[0]);
        Assert.Equal(ErrorCodes.InvalidFile, broken.Request.Error?.Code);
        var getUser = Assert.IsType<RequestFileEntry>(users.Children[1]);
        Assert.Equal("Get user", getUser.Request.Value.Name);
    }

    [Fact]
    public async Task Write_CreatesFoldersAndWritesLfUtf8WithoutBom()
    {
        var path = WorkspacePath.Parse("orders/list-orders.yaml");

        var written = await _repository.WriteRequestAsync(_root, path, new HttpRequestDefinition("List orders", RequestMethod.Get, "/orders"));

        Assert.True(written.Value);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(_root, "orders", "list-orders.yaml"));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);

        var read = await _repository.ReadRequestAsync(_root, path);
        Assert.Equal("List orders", read.Value.Name);
    }

    [Fact]
    public async Task Write_LeavesEquivalentFilesUntouched()
    {
        const string handWritten = "# keep my comment\nname: Health\nurl: '/health'\n";
        Write("health.yaml", handWritten);
        var path = WorkspacePath.Parse("health.yaml");
        var request = (await _repository.ReadRequestAsync(_root, path)).Value;

        var unchanged = await _repository.WriteRequestAsync(_root, path, request);
        Assert.False(unchanged.Value);
        Assert.Equal(handWritten, await File.ReadAllTextAsync(Path.Combine(_root, "health.yaml")));

        var changed = await _repository.WriteRequestAsync(_root, path, request with { Url = "/healthz" });
        Assert.True(changed.Value);
        Assert.Equal("name: Health\nmethod: GET\nurl: /healthz\n", await File.ReadAllTextAsync(Path.Combine(_root, "health.yaml")));
    }

    [Fact]
    public async Task Move_RenamesAndRefusesToOverwrite()
    {
        Write("a.yaml", "name: A\n");
        Write("b.yaml", "name: B\n");

        Assert.False(_repository.MoveFile(_root, WorkspacePath.Parse("a.yaml"), WorkspacePath.Parse("b.yaml")).IsSuccess);
        Assert.True(_repository.MoveFile(_root, WorkspacePath.Parse("a.yaml"), WorkspacePath.Parse("c.yaml")).IsSuccess);
        Assert.True(_repository.FileExists(_root, WorkspacePath.Parse("c.yaml")));
        Assert.False(_repository.FileExists(_root, WorkspacePath.Parse("a.yaml")));
        Assert.Equal("name: B\n", await File.ReadAllTextAsync(Path.Combine(_root, "b.yaml")));
    }

    [Fact]
    public async Task Copy_CopiesBytesAndRefusesToOverwrite()
    {
        Write("a.yaml", "name: A # hand-written\n");
        Write("b.yaml", "name: B\n");

        Assert.False(_repository.CopyFile(_root, WorkspacePath.Parse("a.yaml"), WorkspacePath.Parse("b.yaml")).IsSuccess);
        Assert.True(_repository.CopyFile(_root, WorkspacePath.Parse("a.yaml"), WorkspacePath.Parse("c.yaml")).IsSuccess);
        Assert.Equal("name: A # hand-written\n", await File.ReadAllTextAsync(Path.Combine(_root, "a.yaml")));
        Assert.Equal("name: A # hand-written\n", await File.ReadAllTextAsync(Path.Combine(_root, "c.yaml")));
        Assert.Equal("name: B\n", await File.ReadAllTextAsync(Path.Combine(_root, "b.yaml")));
    }

    [Fact]
    public void FolderContents_ListNamesThatAreNotWorkspacePaths()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows doesn't allow ':' or '\\' in file names.
        }

        Write("users/admin/get.yaml", "name: Get\n");
        Write("users/admin/notes:2026.txt", "x\n");
        Write("users/back\\slash.txt", "x\n");

        var contents = _repository.ReadFolderContents(_root, WorkspacePath.Parse("users")).Value;

        Assert.Equal(["admin/notes:2026.txt", "back\\slash.txt"], contents.Unsupported!.Order(StringComparer.Ordinal));
        Assert.Equal([WorkspacePath.Parse("users/admin/get.yaml")], contents.Files);
    }

    [Fact]
    public async Task Read_ReportsMissingFiles()
    {
        var result = await _repository.ReadRequestAsync(_root, WorkspacePath.Parse("missing.yaml"));

        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
    }

    [Fact]
    public async Task Folders_CanBeCreatedRenamedAndDeleted()
    {
        var users = WorkspacePath.Parse("api/users");
        Assert.True(_repository.CreateFolder(_root, users).IsSuccess);
        Assert.True(_repository.DirectoryExists(_root, users));
        Write("api/users/get-user.yaml", "name: Get user\n");
        Write("api/orders/list.yaml", "name: List\n");

        var people = WorkspacePath.Parse("api/people");
        Assert.True(_repository.MoveFolder(_root, users, people).IsSuccess);
        Assert.False(_repository.EntryExists(_root, users));
        Assert.Equal("name: Get user\n", await File.ReadAllTextAsync(Path.Combine(_root, "api", "people", "get-user.yaml")));

        var clash = _repository.MoveFolder(_root, people, WorkspacePath.Parse("api/orders"));
        Assert.Equal(ErrorCodes.AlreadyExists, clash.Error?.Code);

        Assert.True(_repository.DeleteFolder(_root, people).IsSuccess);
        Assert.False(_repository.EntryExists(_root, people));
        Assert.True(_repository.FileExists(_root, WorkspacePath.Parse("api/orders/list.yaml")));
    }

    [Fact]
    public void MoveFolder_HandlesCaseOnlyRenames()
    {
        Write("users/get-user.yaml", "name: Get user\n");

        var result = _repository.MoveFolder(_root, WorkspacePath.Parse("users"), WorkspacePath.Parse("Users"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["Users"], Directory.GetDirectories(_root).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(_root, "Users", "get-user.yaml")));
    }

    [Fact]
    public void ReadFolderContents_ListsEverythingRecursivelyWithoutFollowingLinks()
    {
        Write("users/get-user.yaml", "name: Get user\n");
        Write("users/admin/notes.md", "hi\n");
        Write("elsewhere/secret.txt", "x\n");
        var linked = false;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_root, "users", "link"), Path.Combine(_root, "elsewhere"));
            linked = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Creating symbolic links needs extra rights on some Windows machines.
        }

        var contents = _repository.ReadFolderContents(_root, WorkspacePath.Parse("users")).Value;

        Assert.Equal(["users/admin/notes.md", "users/get-user.yaml"], contents.Files.Select(f => f.Value).Order());
        Assert.Equal(["users/admin"], contents.Folders.Select(f => f.Value));
        Assert.Equal(linked ? ["users/link"] : [], contents.Links.Select(f => f.Value));
    }

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
