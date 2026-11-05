using Missive.Application.Common;
using Missive.Application.Git;
using Missive.Application.Requests;
using Missive.Domain.Git;
using Missive.Domain.Requests;
using NSubstitute;
using static Missive.Application.Tests.Git.GitTestData;

namespace Missive.Application.Tests.Git;

public class GetFileDiffTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly IRequestFileFormat _format = Substitute.For<IRequestFileFormat>();
    private readonly GetFileDiff _useCase;

    public GetFileDiffTests()
    {
        _useCase = new GetFileDiff(_git, _format, OpenedWorkspace());
    }

    private static HttpRequestDefinition Request(string name, string url) =>
        new(name, RequestMethod.Get, url, [], [], RequestAuth.None, RequestBody.None);

    private void Changed(GitChange change, GitFileDiff diff)
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Status(changes: [change])));
        _git.GetFileDiffAsync(WorkspacePath, change, Arg.Any<CancellationToken>()).Returns(Ok(diff));
    }

    [Fact]
    public async Task AFileWithoutChanges_IsRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Status(changes: [new GitChange("a.yaml", GitFileStatus.Modified)])));

        var result = await _useCase.ExecuteAsync("../secret.txt");

        Assert.Equal(GitErrors.FileNotChangedCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().GetFileDiffAsync(default!, default!, default);
    }

    [Fact]
    public async Task ARequestFile_IsParsedOnBothSides()
    {
        var change = new GitChange("users/get-user.yaml", GitFileStatus.Modified);
        Changed(change, new GitFileDiff(change.Path, change.Status, null, "old", "new"));
        var path = Missive.Domain.Workspaces.WorkspacePath.Parse("users/get-user.yaml");
        _format.Parse("old", path).Returns(Request("Get user", "https://a"));
        _format.Parse("new", path).Returns(Request("Get user", "https://b"));

        var result = await _useCase.ExecuteAsync("users/get-user.yaml");

        Assert.True(result.IsSuccess, result.Error?.Message);
        var dto = result.Value;
        Assert.True(dto.IsRequest);
        Assert.Equal("modified", dto.Status);
        Assert.Equal("old", dto.Original);
        Assert.Equal("new", dto.Modified);
        Assert.Equal("https://a", dto.OriginalRequest?.Url);
        Assert.Equal("https://b", dto.ModifiedRequest?.Url);
        Assert.Null(dto.RequestError);
    }

    [Fact]
    public async Task ARenamedRequest_ParsesTheOriginalAtItsOldPath()
    {
        var change = new GitChange("users/b.yaml", GitFileStatus.Renamed, "users/a.yaml");
        Changed(change, new GitFileDiff(change.Path, change.Status, "users/a.yaml", "old", "new"));
        _format.Parse(default!, default!).ReturnsForAnyArgs(Request("A", "https://a"));

        var result = await _useCase.ExecuteAsync("users/b.yaml");

        Assert.True(result.Value.IsRequest);
        _format.Received(1).Parse("old", Missive.Domain.Workspaces.WorkspacePath.Parse("users/a.yaml"));
        _format.Received(1).Parse("new", Missive.Domain.Workspaces.WorkspacePath.Parse("users/b.yaml"));
    }

    [Fact]
    public async Task AnAddedRequest_HasOnlyTheModifiedSide()
    {
        var change = new GitChange("new.yaml", GitFileStatus.Untracked);
        Changed(change, new GitFileDiff(change.Path, change.Status, null, null, "new"));
        _format.Parse("new", Arg.Any<Missive.Domain.Workspaces.WorkspacePath>()).Returns(Request("New", "https://n"));

        var result = await _useCase.ExecuteAsync("new.yaml");

        Assert.True(result.Value.IsRequest);
        Assert.Null(result.Value.OriginalRequest);
        Assert.Equal("New", result.Value.ModifiedRequest?.Name);
    }

    [Fact]
    public async Task AnInvalidRequestFile_ReportsWhy_AndKeepsTheText()
    {
        var change = new GitChange("broken.yaml", GitFileStatus.Modified);
        Changed(change, new GitFileDiff(change.Path, change.Status, null, "name: A\n", "name: [\n"));
        _format.Parse("name: A\n", Arg.Any<Missive.Domain.Workspaces.WorkspacePath>()).Returns(Request("A", "https://a"));
        _format.Parse("name: [\n", Arg.Any<Missive.Domain.Workspaces.WorkspacePath>())
            .Returns(Result<HttpRequestDefinition>.Failure(ErrorCodes.InvalidFile, "Not valid."));

        var result = await _useCase.ExecuteAsync("broken.yaml");

        Assert.True(result.Value.IsRequest);
        Assert.Equal("Not valid.", result.Value.RequestError);
        Assert.Null(result.Value.ModifiedRequest);
        Assert.Equal("name: [\n", result.Value.Modified);
    }

    [Theory]
    [InlineData("environments/dev.yaml")]
    [InlineData(".gitignore")]
    [InlineData("users/_folder.yaml")]
    public async Task OtherFiles_AreNotParsed(string path)
    {
        var change = new GitChange(path, GitFileStatus.Modified);
        Changed(change, new GitFileDiff(path, change.Status, null, "a", "b"));

        var result = await _useCase.ExecuteAsync(path);

        Assert.False(result.Value.IsRequest);
        Assert.Equal("a", result.Value.Original);
        _format.DidNotReceiveWithAnyArgs().Parse(default!, default!);
    }

    [Fact]
    public async Task BinaryFiles_AreNotParsed()
    {
        var change = new GitChange("odd.yaml", GitFileStatus.Modified);
        Changed(change, new GitFileDiff(change.Path, change.Status, null, null, null, IsBinary: true));

        var result = await _useCase.ExecuteAsync("odd.yaml");

        Assert.True(result.Value.Binary);
        Assert.False(result.Value.IsRequest);
        _format.DidNotReceiveWithAnyArgs().Parse(default!, default!);
    }
}
