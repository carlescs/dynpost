using Dynypost.Application.Common;
using Dynypost.Application.Sharing;
using Dynypost.Application.Settings;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Requests;
using Dynypost.Domain.Workspaces;
using NSubstitute;

namespace Dynypost.Application.Tests;

public class OpenWorkspaceTests
{
    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
    private readonly CurrentWorkspace _current = new();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private AppSettings _storedSettings = AppSettings.Default;

    public OpenWorkspaceTests()
    {
        _localFiles.List(Arg.Any<string>()).Returns(new HashSet<WorkspacePath>());
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _storedSettings = call.Arg<Func<AppSettings, AppSettings>>()(_storedSettings);
                return Task.FromResult<Result<AppSettings>>(_storedSettings);
            });
    }

    [Fact]
    public async Task OpensTheFolderAndBuildsTheTree()
    {
        _repository.FolderExists("/work/my-api").Returns(true);
        _repository.ListAsync("/work/my-api", Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<WorkspaceEntry>>.Success(
        [
            new FolderEntry(WorkspacePath.Parse("users"),
            [
                new RequestFileEntry(WorkspacePath.Parse("users/get-user.yaml"),
                    new HttpRequestDefinition("Get user", RequestMethod.Get, "/users/1")),
            ]),
            new RequestFileEntry(WorkspacePath.Parse("broken.yaml"),
                Result<HttpRequestDefinition>.Failure(ErrorCodes.InvalidFile, "bad yaml")),
        ]));

        var result = await new OpenWorkspace(_repository, _localFiles, _current, _settings, Workspaces.Migrations.UpToDate()).ExecuteAsync("/work/my-api");

        Assert.True(result.IsSuccess);
        Assert.Equal("/work/my-api", _current.Root);
        Assert.Equal("my-api", result.Value.Name);

        var folder = result.Value.Nodes[0];
        Assert.Equal(("users", "users", "folder"), (folder.Path, folder.Name, folder.Kind));
        var request = Assert.Single(folder.Children!);
        Assert.Equal(("users/get-user.yaml", "Get user", "GET"), (request.Path, request.Name, request.Method));

        var broken = result.Value.Nodes[1];
        Assert.Equal(("broken.yaml", "request", "bad yaml"), (broken.Name, broken.Kind, broken.Error));
    }

    [Fact]
    public async Task FailsForAMissingFolderAndKeepsThePreviousWorkspace()
    {
        _current.Open("/work/previous");
        _repository.FolderExists("/nope").Returns(false);

        var result = await new OpenWorkspace(_repository, _localFiles, _current, _settings, Workspaces.Migrations.UpToDate()).ExecuteAsync("/nope");

        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
        Assert.Equal("/work/previous", _current.Root);
    }

    [Fact]
    public async Task GetWorkspaceTree_RequiresAnOpenWorkspace()
    {
        var result = await new GetWorkspaceTree(_repository, _localFiles, _current).ExecuteAsync();

        Assert.Equal(ErrorCodes.NoWorkspace, result.Error?.Code);
    }
}
