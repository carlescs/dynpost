using Missive.Application.Common;
using Missive.Application.Git;
using Missive.Application.Workspaces;
using Missive.Domain.Git;
using NSubstitute;
using static Missive.Application.Tests.Git.GitTestData;

namespace Missive.Application.Tests.Git;

public class BranchUseCasesTests
{
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly CurrentWorkspace _workspace = OpenedWorkspace();

    private static readonly GitBranch Main = new("main", null, true, "origin/main", "a1", null, "Init");
    private static readonly GitBranch Feature = new("feature", null, false, null, "b2", null, "Work");
    private static readonly GitBranch RemoteMain = new("origin/main", "origin", false, null, "a1", null, "Init");
    private static readonly GitBranch RemoteTopic = new("origin/topic", "origin", false, null, "c3", null, "Topic");

    public BranchUseCasesTests()
    {
        HasStatus(Clean());
        _git.ListBranchesAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<GitBranch>>([Main, Feature, RemoteMain, RemoteTopic]));
        _git.SwitchBranchAsync(default!, default!).ReturnsForAnyArgs(Ok(Unit.Value));
        _git.CreateBranchAsync(default!, default!, default).ReturnsForAnyArgs(Ok(Unit.Value));
        _git.CheckoutRemoteBranchAsync(default!, default!, default!).ReturnsForAnyArgs(Ok(Unit.Value));
        _git.DeleteBranchAsync(default!, default!, default).ReturnsForAnyArgs(Ok(Unit.Value));
        _git.MergeBranchAsync(default!, default!).ReturnsForAnyArgs(Ok(BranchMergeResult.Of(MergeOutcome.Merged)));
    }

    private void HasStatus(GitStatus status) =>
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(status));

    private static GitStatus WithChanges() =>
        Status(changes: [new GitChange("users/get.yaml", GitFileStatus.Modified), new GitChange("new.yaml", GitFileStatus.Untracked)]);

    [Fact]
    public async Task List_ReturnsTheStatusAndBranches()
    {
        var result = await new ListBranches(_git, _workspace).ExecuteAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(4, result.Value.Branches.Count);
        Assert.Equal("main", result.Value.Status.Branch);
    }

    [Fact]
    public async Task List_WithoutAWorkspace_IsRejected()
    {
        var result = await new ListBranches(_git, new CurrentWorkspace()).ExecuteAsync();

        Assert.Equal(ErrorCodes.NoWorkspace, result.Error?.Code);
    }

    [Fact]
    public async Task Switch_ChecksOutTheBranch()
    {
        var result = await new SwitchBranch(_git, _workspace).ExecuteAsync(" feature ");

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _git.Received(1).SwitchBranchAsync(WorkspacePath, "feature", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Switch_WithUncommittedChanges_IsRefusedAndNamesThem()
    {
        HasStatus(WithChanges());

        var result = await new SwitchBranch(_git, _workspace).ExecuteAsync("feature");

        Assert.Equal(GitErrors.UncommittedChangesCode, result.Error?.Code);
        Assert.Contains("users/get.yaml, new.yaml", result.Error!.Message);
        await _git.DidNotReceiveWithAnyArgs().SwitchBranchAsync(default!, default!);
    }

    [Fact]
    public async Task Switch_DuringAMerge_IsRefused()
    {
        HasStatus(Status(merging: true));

        var result = await new SwitchBranch(_git, _workspace).ExecuteAsync("feature");

        Assert.Equal(GitErrors.BranchDuringMergeCode, result.Error?.Code);
    }

    [Theory]
    [InlineData("origin/topic")]
    [InlineData("nope")]
    [InlineData("-f")]
    public async Task Switch_OnlyToExistingLocalBranches(string name)
    {
        var result = await new SwitchBranch(_git, _workspace).ExecuteAsync(name);

        Assert.Equal(GitErrors.BranchNotFoundCode, result.Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().SwitchBranchAsync(default!, default!);
    }

    [Fact]
    public async Task Switch_ToTheCurrentBranch_DoesNothing()
    {
        HasStatus(WithChanges());

        var result = await new SwitchBranch(_git, _workspace).ExecuteAsync("main");

        Assert.True(result.IsSuccess);
        await _git.DidNotReceiveWithAnyArgs().SwitchBranchAsync(default!, default!);
    }

    [Fact]
    public async Task Create_ValidatesTheNameAndRefusesDuplicates()
    {
        var create = new CreateBranch(_git, _workspace);

        Assert.Equal(GitErrors.InvalidBranchNameCode, (await create.ExecuteAsync(new CreateBranchRequest("two words", false))).Error?.Code);
        Assert.Equal(GitErrors.BranchExistsCode, (await create.ExecuteAsync(new CreateBranchRequest("Feature", false))).Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().CreateBranchAsync(default!, default!, default);
    }

    [Fact]
    public async Task Create_AllowsUncommittedChanges()
    {
        HasStatus(WithChanges());

        var result = await new CreateBranch(_git, _workspace).ExecuteAsync(new CreateBranchRequest(" new/thing ", true));

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _git.Received(1).CreateBranchAsync(
            WorkspacePath,
            Arg.Is<BranchName>(n => n.Value == "new/thing"),
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WithoutCommits_IsRefused()
    {
        HasStatus(Status() with { IsUnborn = true });

        var result = await new CreateBranch(_git, _workspace).ExecuteAsync(new CreateBranchRequest("next", false));

        Assert.Equal(GitErrors.NoCommitsCode, result.Error?.Code);
    }

    [Fact]
    public async Task CheckoutRemote_CreatesATrackingBranchNamedAfterIt()
    {
        var result = await new CheckoutRemoteBranch(_git, _workspace).ExecuteAsync("origin/topic");

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _git.Received(1).CheckoutRemoteBranchAsync(
            WorkspacePath,
            "origin/topic",
            Arg.Is<BranchName>(n => n.Value == "topic"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckoutRemote_WhenTheLocalBranchExists_IsRefused()
    {
        var result = await new CheckoutRemoteBranch(_git, _workspace).ExecuteAsync("origin/main");

        Assert.Equal(GitErrors.BranchExistsCode, result.Error?.Code);
        Assert.Contains("Switch to it", result.Error!.Message);
    }

    [Fact]
    public async Task CheckoutRemote_WithUncommittedChanges_IsRefused()
    {
        HasStatus(WithChanges());

        var result = await new CheckoutRemoteBranch(_git, _workspace).ExecuteAsync("origin/topic");

        Assert.Equal(GitErrors.UncommittedChangesCode, result.Error?.Code);
    }

    [Fact]
    public async Task Delete_PassesForceOnlyWhenAsked()
    {
        await new DeleteBranch(_git, _workspace).ExecuteAsync(new DeleteBranchRequest("feature"));
        await new DeleteBranch(_git, _workspace).ExecuteAsync(new DeleteBranchRequest("feature", Force: true));

        Received.InOrder(() =>
        {
            _git.DeleteBranchAsync(WorkspacePath, "feature", false, Arg.Any<CancellationToken>());
            _git.DeleteBranchAsync(WorkspacePath, "feature", true, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Delete_NeverTheCurrentOrARemoteBranch()
    {
        var delete = new DeleteBranch(_git, _workspace);

        Assert.Equal(GitErrors.CurrentBranchCode, (await delete.ExecuteAsync(new DeleteBranchRequest("main"))).Error?.Code);
        Assert.Equal(GitErrors.BranchNotFoundCode, (await delete.ExecuteAsync(new DeleteBranchRequest("origin/topic"))).Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().DeleteBranchAsync(default!, default!, default);
    }

    [Fact]
    public async Task Delete_ReportsUnmergedBranches()
    {
        _git.DeleteBranchAsync(WorkspacePath, "feature", false, Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Failure(GitErrors.BranchNotMerged("feature")));

        var result = await new DeleteBranch(_git, _workspace).ExecuteAsync(new DeleteBranchRequest("feature"));

        Assert.Equal(GitErrors.BranchNotMergedCode, result.Error?.Code);
    }

    [Theory]
    [InlineData(" feature ", false)]
    [InlineData("origin/topic", true)]
    public async Task Merge_MergesALocalOrRemoteBranchAndReturnsTheNewState(string name, bool remote)
    {
        var result = await new MergeBranch(_git, _workspace).ExecuteAsync(new MergeBranchRequest(name, remote));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(MergeOutcome.Merged, result.Value.Outcome);
        Assert.Equal(4, result.Value.Branches.Count);
        await _git.Received(1).MergeBranchAsync(
            WorkspacePath,
            Arg.Is<GitBranch>(b => b.Name == name.Trim() && b.IsRemote == remote),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Merge_TellsALocalBranchFromARemoteOneOfTheSameName()
    {
        var localNamedLikeRemote = new GitBranch("origin/topic", null, false, null, "d4", null, "Local");
        _git.ListBranchesAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<GitBranch>>([Main, localNamedLikeRemote, RemoteTopic]));
        var merge = new MergeBranch(_git, _workspace);

        await merge.ExecuteAsync(new MergeBranchRequest("origin/topic", Remote: true));
        await merge.ExecuteAsync(new MergeBranchRequest("origin/topic"));

        Received.InOrder(() =>
        {
            _git.MergeBranchAsync(WorkspacePath, RemoteTopic, Arg.Any<CancellationToken>());
            _git.MergeBranchAsync(WorkspacePath, localNamedLikeRemote, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Merge_ReportsConflictsIncludingThoseOutsideTheWorkspace()
    {
        _git.MergeBranchAsync(WorkspacePath, Feature, Arg.Any<CancellationToken>())
            .Returns(Ok(new BranchMergeResult(MergeOutcome.Conflicted, ["docs/notes.md"])));

        var result = await new MergeBranch(_git, _workspace).ExecuteAsync(new MergeBranchRequest("feature"));

        Assert.Equal(MergeOutcome.Conflicted, result.Value.Outcome);
        Assert.Equal(["docs/notes.md"], result.Value.ConflictsOutsideWorkspace);
    }

    [Fact]
    public async Task Merge_UnknownOrCurrentBranch_IsRejected()
    {
        var merge = new MergeBranch(_git, _workspace);

        Assert.Equal(GitErrors.BranchNotFoundCode, (await merge.ExecuteAsync(new MergeBranchRequest("nope"))).Error?.Code);
        Assert.Equal(GitErrors.BranchNotFoundCode, (await merge.ExecuteAsync(new MergeBranchRequest("feature", Remote: true))).Error?.Code);
        Assert.Equal(GitErrors.MergeSameBranchCode, (await merge.ExecuteAsync(new MergeBranchRequest("main"))).Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().MergeBranchAsync(default!, default!);
    }

    [Fact]
    public async Task Merge_WithUncommittedChanges_IsRefusedAndNamesTheFiles()
    {
        HasStatus(WithChanges());

        var result = await new MergeBranch(_git, _workspace).ExecuteAsync(new MergeBranchRequest("feature"));

        Assert.Equal(GitErrors.UncommittedChangesCode, result.Error?.Code);
        Assert.Contains("users/get.yaml", result.Error!.Message);
        Assert.Contains("new.yaml", result.Error.Message);
        await _git.DidNotReceiveWithAnyArgs().MergeBranchAsync(default!, default!);
    }

    [Fact]
    public async Task Merge_DuringAMergeOnADetachedHeadOrWithoutCommits_IsRefused()
    {
        var merge = new MergeBranch(_git, _workspace);

        HasStatus(Status(merging: true));
        Assert.Equal(GitErrors.BranchDuringMergeCode, (await merge.ExecuteAsync(new MergeBranchRequest("feature"))).Error?.Code);
        HasStatus(Status(branch: null, upstream: null));
        Assert.Equal(GitErrors.DetachedHeadCode, (await merge.ExecuteAsync(new MergeBranchRequest("feature"))).Error?.Code);
        HasStatus(Status(upstream: null) with { IsUnborn = true });
        Assert.Equal(GitErrors.NoCommitsCode, (await merge.ExecuteAsync(new MergeBranchRequest("feature"))).Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().MergeBranchAsync(default!, default!);
    }

    [Fact]
    public async Task Merge_PassesGitFailuresOn()
    {
        _git.MergeBranchAsync(WorkspacePath, Feature, Arg.Any<CancellationToken>())
            .Returns(Result<BranchMergeResult>.Failure(GitErrors.IdentityUnknown()));

        var result = await new MergeBranch(_git, _workspace).ExecuteAsync(new MergeBranchRequest("feature"));

        Assert.Equal(GitErrors.IdentityUnknownCode, result.Error?.Code);
    }
}
