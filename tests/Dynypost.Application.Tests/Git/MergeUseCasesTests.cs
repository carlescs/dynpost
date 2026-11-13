using Dynypost.Application.Common;
using Dynypost.Application.Git;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Git;
using NSubstitute;
using static Dynypost.Application.Tests.Git.GitTestData;

namespace Dynypost.Application.Tests.Git;

public class MergeUseCasesTests
{
    private const string Markers = "<<<<<<< HEAD\nname: Bob\n=======\nname: Alice\n>>>>>>> origin/main\n";

    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly CurrentWorkspace _workspace = OpenedWorkspace();

    private static GitStatus Merging(params GitChange[] changes) => Status(merging: true, changes: changes);

    [Fact]
    public async Task Resolve_WithoutAMerge_IsRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Clean()));

        var result = await new ResolveConflict(_git, _workspace)
            .ExecuteAsync(new ResolveConflictRequest("a.yaml", ConflictResolution.Ours));

        Assert.Equal(GitErrors.NotMergingCode, result.Error?.Code);
    }

    [Fact]
    public async Task Resolve_AFileThatIsNotConflicted_IsRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Merging(new GitChange("a.yaml", GitFileStatus.Modified))));

        var result = await new ResolveConflict(_git, _workspace)
            .ExecuteAsync(new ResolveConflictRequest("a.yaml", ConflictResolution.Theirs));

        Assert.Equal(GitErrors.NotConflictedCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().ResolveConflictAsync(default!, default!, default, default);
    }

    [Theory]
    [InlineData(ConflictResolution.Ours)]
    [InlineData(ConflictResolution.Theirs)]
    public async Task Resolve_TakingASide_ResolvesAndReturnsTheNewStatus(ConflictResolution resolution)
    {
        var after = Merging(new GitChange("a.yaml", GitFileStatus.Modified));
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Merging(new GitChange("a.yaml", GitFileStatus.Conflicted))), Ok(after));
        _git.ResolveConflictAsync(default!, default!, default, default).ReturnsForAnyArgs(Ok(Unit.Value));

        var result = await new ResolveConflict(_git, _workspace).ExecuteAsync(new ResolveConflictRequest("a.yaml", resolution));

        Assert.Same(after, result.Value);
        _ = _git.Received(1).ResolveConflictAsync(WorkspacePath, "a.yaml", resolution, Arg.Any<CancellationToken>());
        _ = _git.DidNotReceiveWithAnyArgs().GetConflictAsync(default!, default!, default);
    }

    [Fact]
    public async Task Resolve_AsEdited_WithMarkersLeft_IsRefused()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Merging(new GitChange("a.yaml", GitFileStatus.Conflicted))));
        _git.GetConflictAsync(WorkspacePath, "a.yaml", Arg.Any<CancellationToken>())
            .Returns(Ok(new GitConflict("a.yaml", "name: A\n", "name: Bob\n", "name: Alice\n", Markers)));

        var result = await new ResolveConflict(_git, _workspace)
            .ExecuteAsync(new ResolveConflictRequest("a.yaml", ConflictResolution.Edited));

        Assert.Equal(GitErrors.ConflictMarkersRemainCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().ResolveConflictAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Resolve_AsEdited_WithoutMarkers_MarksItResolved()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Merging(new GitChange("a.yaml", GitFileStatus.Conflicted))), Ok(Merging()));
        _git.GetConflictAsync(WorkspacePath, "a.yaml", Arg.Any<CancellationToken>())
            .Returns(Ok(new GitConflict("a.yaml", "name: A\n", "name: Bob\n", "name: Alice\n", "name: Bob and Alice\n")));
        _git.ResolveConflictAsync(default!, default!, default, default).ReturnsForAnyArgs(Ok(Unit.Value));

        var result = await new ResolveConflict(_git, _workspace)
            .ExecuteAsync(new ResolveConflictRequest("a.yaml", ConflictResolution.Edited));

        Assert.True(result.IsSuccess, result.Error?.Message);
        _ = _git.Received(1).ResolveConflictAsync(WorkspacePath, "a.yaml", ConflictResolution.Edited, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetConflict_OnlyReadsConflictedFiles()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Merging(new GitChange("a.yaml", GitFileStatus.Conflicted))));

        var other = await new GetConflict(_git, _workspace).ExecuteAsync("../secrets.yaml");

        Assert.Equal(GitErrors.NotConflictedCode, other.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().GetConflictAsync(default!, default!, default);
    }

    [Fact]
    public async Task Abort_AbortsAndReturnsTheNewStatus()
    {
        var after = Clean();
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>())
            .Returns(Ok(Merging(new GitChange("a.yaml", GitFileStatus.Conflicted))), Ok(after));
        _git.AbortMergeAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Unit.Value));

        var result = await new AbortMerge(_git, _workspace).ExecuteAsync();

        Assert.Same(after, result.Value);
    }

    [Fact]
    public async Task Abort_WithoutAMerge_IsRejected()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Clean()));

        var result = await new AbortMerge(_git, _workspace).ExecuteAsync();

        Assert.Equal(GitErrors.NotMergingCode, result.Error?.Code);
        _ = _git.DidNotReceiveWithAnyArgs().AbortMergeAsync(default!, default);
    }
}
