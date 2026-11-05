using Missive.Application.Common;
using Missive.Application.Git;
using Missive.Application.Workspaces;
using Missive.Domain.Git;
using NSubstitute;
using static Missive.Application.Tests.Git.GitTestData;

namespace Missive.Application.Tests.Git;

public class HistoryUseCasesTests
{
    private const string Head = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Parent = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly CurrentWorkspace _workspace = OpenedWorkspace();

    private static GitCommitInfo Commit(string id, params string[] parents) =>
        new(id, parents, "Alice", "alice@example.com", DateTimeOffset.UnixEpoch, $"Commit {id[..4]}");

    private void HasCommit(params GitChange[] files) =>
        _git.GetCommitAsync(WorkspacePath, Head, Arg.Any<CancellationToken>())
            .Returns(Ok(new GitCommitDetails(Commit(Head, Parent), "", files)));

    [Fact]
    public async Task History_WithoutAWorkspace_IsRejected()
    {
        var result = await new GetGitHistory(_git, new CurrentWorkspace()).ExecuteAsync(new GetGitHistoryRequest());

        Assert.Equal(ErrorCodes.NoWorkspace, result.Error?.Code);
    }

    [Fact]
    public async Task History_AsksForOneMoreCommit_ToTellWhetherMoreFollow()
    {
        IReadOnlyList<GitCommitInfo> log = [Commit(Head, Parent), Commit(Parent)];
        _git.GetLogAsync(WorkspacePath, 10, 2, Arg.Any<CancellationToken>()).Returns(Ok(log));

        var result = await new GetGitHistory(_git, _workspace).ExecuteAsync(new GetGitHistoryRequest(10, 1));

        Assert.True(result.Value.HasMore);
        Assert.Equal([Head], result.Value.Commits.Select(c => c.Id));
    }

    [Fact]
    public async Task History_LastPage_HasNoMore()
    {
        IReadOnlyList<GitCommitInfo> log = [Commit(Head, Parent)];
        _git.GetLogAsync(default!, default, default, default).ReturnsForAnyArgs(Ok(log));

        var result = await new GetGitHistory(_git, _workspace).ExecuteAsync(new GetGitHistoryRequest(-5, 100_000));

        Assert.False(result.Value.HasMore);
        Assert.Single(result.Value.Commits);
        _ = _git.Received(1).GetLogAsync(WorkspacePath, 0, GetGitHistory.MaxLimit + 1, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("--all")]
    [InlineData("abc")]
    [InlineData("main~1")]
    public async Task Commit_WithAnIdThatIsNotAHash_IsRejected(string commitId)
    {
        var result = await new GetCommit(_git, _workspace).ExecuteAsync(commitId);

        Assert.Equal(GitErrors.CommitNotFoundCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().GetCommitAsync(default!, default!, default);
    }

    [Fact]
    public async Task CommitFile_NotChangedInTheCommit_IsRejected()
    {
        HasCommit(new GitChange("a.yaml", GitFileStatus.Modified));

        var result = await new GetCommitFile(_git, _workspace).ExecuteAsync(new GetCommitFileRequest(Head, "../secret.txt"));

        Assert.Equal(GitErrors.FileNotInCommitCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().ReadFileAtCommitAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task CommitFile_Modified_ReadsTheParentAndTheCommitVersions()
    {
        HasCommit(new GitChange("a.yaml", GitFileStatus.Modified));
        _git.ReadFileAtCommitAsync(WorkspacePath, Parent, "a.yaml", Arg.Any<CancellationToken>()).Returns(Ok<string?>("name: A\n"));
        _git.ReadFileAtCommitAsync(WorkspacePath, Head, "a.yaml", Arg.Any<CancellationToken>()).Returns(Ok<string?>("name: B\n"));

        var result = await new GetCommitFile(_git, _workspace).ExecuteAsync(new GetCommitFileRequest(Head, "a.yaml"));

        Assert.Equal("name: A\n", result.Value.Before);
        Assert.Equal("name: B\n", result.Value.After);
        Assert.False(result.Value.IsBinary);
    }

    [Fact]
    public async Task CommitFile_Renamed_ReadsTheOldPathInTheParent()
    {
        HasCommit(new GitChange("new.yaml", GitFileStatus.Renamed, "old.yaml"));
        _git.ReadFileAtCommitAsync(WorkspacePath, Parent, "old.yaml", Arg.Any<CancellationToken>()).Returns(Ok<string?>("old\n"));
        _git.ReadFileAtCommitAsync(WorkspacePath, Head, "new.yaml", Arg.Any<CancellationToken>()).Returns(Ok<string?>("new\n"));

        var result = await new GetCommitFile(_git, _workspace).ExecuteAsync(new GetCommitFileRequest(Head, "new.yaml"));

        Assert.Equal("old\n", result.Value.Before);
        Assert.Equal("new\n", result.Value.After);
    }

    [Fact]
    public async Task CommitFile_AddedOrDeleted_HasOnlyOneSide()
    {
        HasCommit(new GitChange("added.yaml", GitFileStatus.Added), new GitChange("gone.yaml", GitFileStatus.Deleted));
        _git.ReadFileAtCommitAsync(WorkspacePath, Head, "added.yaml", Arg.Any<CancellationToken>()).Returns(Ok<string?>("new\n"));
        _git.ReadFileAtCommitAsync(WorkspacePath, Parent, "gone.yaml", Arg.Any<CancellationToken>()).Returns(Ok<string?>("old\n"));
        var useCase = new GetCommitFile(_git, _workspace);

        var added = await useCase.ExecuteAsync(new GetCommitFileRequest(Head, "added.yaml"));
        var deleted = await useCase.ExecuteAsync(new GetCommitFileRequest(Head, "gone.yaml"));

        Assert.Null(added.Value.Before);
        Assert.Equal("new\n", added.Value.After);
        Assert.Equal("old\n", deleted.Value.Before);
        Assert.Null(deleted.Value.After);
        _ = _git.DidNotReceive().ReadFileAtCommitAsync(WorkspacePath, Parent, "added.yaml", Arg.Any<CancellationToken>());
        _ = _git.DidNotReceive().ReadFileAtCommitAsync(WorkspacePath, Head, "gone.yaml", Arg.Any<CancellationToken>());
    }
}
