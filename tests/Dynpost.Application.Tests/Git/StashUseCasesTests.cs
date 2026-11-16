using Dynpost.Application.Common;
using Dynpost.Application.Git;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;
using NSubstitute;
using static Dynpost.Application.Tests.Git.GitTestData;

namespace Dynpost.Application.Tests.Git;

public class StashUseCasesTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly CurrentWorkspace _workspace = OpenedWorkspace();

    private static readonly GitStash Newest = new(0, "aaa111", "Half-done login", "main", null);
    private static readonly GitStash Older = new(1, "bbb222", "abc1234 Init", "feature", null);

    public StashUseCasesTests()
    {
        HasStatus(Clean());
        _git.ListStashesAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<GitStash>>([Newest, Older]));
        _git.StashAsync(default!, default).ReturnsForAnyArgs(Ok(Unit.Value));
        _git.ApplyStashAsync(default!, default!, default).ReturnsForAnyArgs(Ok(StashApplyResult.Of(StashApplyOutcome.Applied)));
        _git.DropStashAsync(default!, default!).ReturnsForAnyArgs(Ok(Unit.Value));
    }

    private void HasStatus(GitStatus status) =>
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(status));

    private static GitStatus WithChanges() =>
        Status(changes: [new GitChange("users/get.yaml", GitFileStatus.Modified), new GitChange("new.yaml", GitFileStatus.Untracked)]);

    [Fact]
    public async Task List_ReturnsTheStatusAndStashes()
    {
        var result = await new ListStashes(_git, _workspace).ExecuteAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal([Newest, Older], result.Value.Stashes);
        Assert.Equal("main", result.Value.Status.Branch);
    }

    [Fact]
    public async Task List_WithoutAWorkspace_IsRejected()
    {
        var result = await new ListStashes(_git, new CurrentWorkspace()).ExecuteAsync();

        Assert.Equal(ErrorCodes.NoWorkspace, result.Error?.Code);
    }

    [Fact]
    public async Task Stash_StashesTheChangesWithTheMessageOnOneLine()
    {
        HasStatus(WithChanges());

        var result = await new StashChanges(_git, _workspace).ExecuteAsync(new StashChangesRequest("  Login\nwork  "));

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _git.Received(1).StashAsync(WorkspacePath, "Login work", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stash_WithABlankMessage_LetsGitDescribeIt()
    {
        HasStatus(WithChanges());

        var result = await new StashChanges(_git, _workspace).ExecuteAsync(new StashChangesRequest("  "));

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _git.Received(1).StashAsync(WorkspacePath, null, Arg.Any<CancellationToken>());
    }

    public static TheoryData<GitStatus, string> StashRefusals() => new()
    {
        { Clean(), GitErrors.NothingToStashCode },
        { Status(changes: [new GitChange("a.yaml", GitFileStatus.Untracked)]) with { IsUnborn = true }, GitErrors.NoCommitsCode },
        { Status(merging: true, changes: [new GitChange("a.yaml", GitFileStatus.Modified)]), GitErrors.BranchDuringMergeCode },
        { Status(changes: [new GitChange("a.yaml", GitFileStatus.Conflicted)]), GitErrors.UnresolvedConflictsCode },
    };

    [Theory]
    [MemberData(nameof(StashRefusals))]
    public async Task Stash_IsRefused_WhenThereIsNothingToSetAsideSafely(GitStatus status, string code)
    {
        HasStatus(status);

        var result = await new StashChanges(_git, _workspace).ExecuteAsync(new StashChangesRequest());

        Assert.Equal(code, result.Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().StashAsync(default!, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_AppliesTheEntryNamedById(bool pop)
    {
        var result = await new ApplyStash(_git, _workspace).ExecuteAsync(new ApplyStashRequest(" BBB222 ", pop));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(StashApplyOutcome.Applied, result.Value.Outcome);
        await _git.Received(1).ApplyStashAsync(WorkspacePath, Older, pop, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_ThatConflicts_ReportsItWithTheStatusAfterwards()
    {
        _git.ApplyStashAsync(default!, default!, default)
            .ReturnsForAnyArgs(Ok(new StashApplyResult(StashApplyOutcome.Conflicted, ["other/x.txt"])));
        var conflicted = Status(changes: [new GitChange("a.yaml", GitFileStatus.Conflicted)]);
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Clean()), Ok(conflicted));

        var result = await new ApplyStash(_git, _workspace).ExecuteAsync(new ApplyStashRequest("aaa111", Pop: true));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(StashApplyOutcome.Conflicted, result.Value.Outcome);
        Assert.Equal(conflicted, result.Value.Status);
        Assert.Equal(["other/x.txt"], result.Value.ConflictsOutsideWorkspace);
    }

    [Fact]
    public async Task Apply_WithUncommittedChanges_IsRefusedAndNamesThem()
    {
        HasStatus(WithChanges());

        var result = await new ApplyStash(_git, _workspace).ExecuteAsync(new ApplyStashRequest("aaa111"));

        Assert.Equal(GitErrors.UncommittedChangesCode, result.Error?.Code);
        Assert.Contains("users/get.yaml, new.yaml", result.Error!.Message);
        await _git.DidNotReceiveWithAnyArgs().ApplyStashAsync(default!, default!, default);
    }

    [Fact]
    public async Task Apply_DuringAMerge_IsRefused()
    {
        HasStatus(Status(merging: true));

        var result = await new ApplyStash(_git, _workspace).ExecuteAsync(new ApplyStashRequest("aaa111"));

        Assert.Equal(GitErrors.BranchDuringMergeCode, result.Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().ApplyStashAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("ccc333")]
    [InlineData("")]
    public async Task Apply_AnUnknownEntry_IsRejected(string id)
    {
        var result = await new ApplyStash(_git, _workspace).ExecuteAsync(new ApplyStashRequest(id));

        Assert.Equal(GitErrors.StashNotFoundCode, result.Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().ApplyStashAsync(default!, default!, default);
    }

    [Fact]
    public async Task Drop_DropsTheEntryNamedById()
    {
        var result = await new DropStash(_git, _workspace).ExecuteAsync("aaa111");

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _git.Received(1).DropStashAsync(WorkspacePath, Newest, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drop_AnUnknownEntry_IsRejected()
    {
        var result = await new DropStash(_git, _workspace).ExecuteAsync("ccc333");

        Assert.Equal(GitErrors.StashNotFoundCode, result.Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().DropStashAsync(default!, default!);
    }

    [Fact]
    public async Task Drop_PassesOnGitFailures()
    {
        _git.DropStashAsync(default!, default!).ReturnsForAnyArgs(GitErrors.StashNotFound());

        var result = await new DropStash(_git, _workspace).ExecuteAsync("aaa111");

        Assert.Equal(GitErrors.StashNotFoundCode, result.Error?.Code);
    }
}
