using Dynpost.Application.Git;
using Dynpost.Domain.Git;
using Dynpost.Infrastructure.Git;

namespace Dynpost.Infrastructure.Tests.Git;

/// <summary>Stashes changes with <see cref="GitCliService"/> in real temporary repositories.</summary>
public sealed class GitStashTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly GitCliService _git = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Stash_SetsAsideOnlyTheWorkspaceChanges_AndPopBringsThemBack()
    {
        var (repository, workspace) = InitRepositoryWithWorkspace();
        Write(workspace, "a.yaml", "name: A2\n");
        Write(workspace, "users/new.yaml", "name: New\n");
        Write(workspace, ".dynpost/secrets.yaml", "token: secret\n");
        Write(repository, "README.md", "# Changed outside the workspace\n");

        var stashed = await _git.StashAsync(workspace, "Login work");

        Assert.True(stashed.IsSuccess, stashed.Error?.Message);
        Assert.Empty((await _git.GetStatusAsync(workspace)).Value.Changes);
        Assert.Equal("name: A\n", Read(workspace, "a.yaml"));
        Assert.False(File.Exists(Path.Combine(workspace, "users/new.yaml")));
        Assert.Equal("token: secret\n", Read(workspace, ".dynpost/secrets.yaml"));
        Assert.Equal("# Changed outside the workspace\n", Read(repository, "README.md"));

        var list = await _git.ListStashesAsync(workspace);
        Assert.True(list.IsSuccess, list.Error?.Message);
        var stash = Assert.Single(list.Value);
        Assert.Equal(0, stash.Index);
        Assert.Equal(TestGit.Run(repository, "rev-parse", "stash@{0}").Trim(), stash.Id);
        Assert.Equal("Login work", stash.Message);
        Assert.Equal("main", stash.Branch);
        Assert.NotNull(stash.CreatedAt);

        var popped = await _git.ApplyStashAsync(workspace, stash, pop: true);

        Assert.True(popped.IsSuccess, popped.Error?.Message);
        Assert.Equal(StashApplyOutcome.Applied, popped.Value.Outcome);
        Assert.Equal("name: A2\n", Read(workspace, "a.yaml"));
        Assert.Equal("name: New\n", Read(workspace, "users/new.yaml"));
        Assert.Empty((await _git.ListStashesAsync(workspace)).Value);
    }

    [Fact]
    public async Task Stash_WithoutAMessage_IsDescribedByTheLastCommit()
    {
        var (_, workspace) = InitRepositoryWithWorkspace();
        Write(workspace, "a.yaml", "name: A2\n");

        Assert.True((await _git.StashAsync(workspace, null)).IsSuccess);

        var stash = Assert.Single((await _git.ListStashesAsync(workspace)).Value);
        Assert.EndsWith(" Initial", stash.Message);
        Assert.Equal("main", stash.Branch);
    }

    [Fact]
    public async Task Apply_KeepsTheEntry_AndDropRemovesOnlyThatOne()
    {
        var (_, workspace) = InitRepositoryWithWorkspace();
        Write(workspace, "a.yaml", "name: First\n");
        Assert.True((await _git.StashAsync(workspace, "First")).IsSuccess);
        Write(workspace, "a.yaml", "name: Second\n");
        Assert.True((await _git.StashAsync(workspace, "Second")).IsSuccess);
        var stashes = (await _git.ListStashesAsync(workspace)).Value;
        Assert.Equal(["Second", "First"], stashes.Select(s => s.Message));

        var applied = await _git.ApplyStashAsync(workspace, stashes[1], pop: false);

        Assert.True(applied.IsSuccess, applied.Error?.Message);
        Assert.Equal("name: First\n", Read(workspace, "a.yaml"));
        Assert.Equal(2, (await _git.ListStashesAsync(workspace)).Value.Count);

        var dropped = await _git.DropStashAsync(workspace, stashes[0]);

        Assert.True(dropped.IsSuccess, dropped.Error?.Message);
        var left = Assert.Single((await _git.ListStashesAsync(workspace)).Value);
        Assert.Equal(stashes[1].Id, left.Id);
        Assert.Equal(0, left.Index);
    }

    [Fact]
    public async Task AnEntryThatMovedInTheList_IsNotActedOn()
    {
        var (_, workspace) = InitRepositoryWithWorkspace();
        Write(workspace, "a.yaml", "name: First\n");
        Assert.True((await _git.StashAsync(workspace, "First")).IsSuccess);
        var first = Assert.Single((await _git.ListStashesAsync(workspace)).Value);
        // Another git tool stashes meanwhile: the first entry is now stash@{1}.
        Write(workspace, "a.yaml", "name: Second\n");
        TestGit.Run(workspace, "stash", "push", "--quiet");

        var dropped = await _git.DropStashAsync(workspace, first);
        var applied = await _git.ApplyStashAsync(workspace, first, pop: true);

        Assert.Equal(GitErrors.StashNotFoundCode, dropped.Error?.Code);
        Assert.Equal(GitErrors.StashNotFoundCode, applied.Error?.Code);
        Assert.Equal(2, (await _git.ListStashesAsync(workspace)).Value.Count);
    }

    [Fact]
    public async Task Pop_ThatConflicts_LeavesTheConflictToResolve_AndKeepsTheEntry()
    {
        var (repository, workspace) = InitRepositoryWithWorkspace();
        Write(workspace, "a.yaml", "name: Stashed\n");
        Assert.True((await _git.StashAsync(workspace, "Mine")).IsSuccess);
        Write(workspace, "a.yaml", "name: Committed\n");
        TestGit.CommitAll(repository, "Change A");
        var stash = Assert.Single((await _git.ListStashesAsync(workspace)).Value);

        var popped = await _git.ApplyStashAsync(workspace, stash, pop: true);

        Assert.True(popped.IsSuccess, popped.Error?.Message);
        Assert.Equal(StashApplyOutcome.Conflicted, popped.Value.Outcome);
        Assert.Empty(popped.Value.ConflictsOutsideWorkspace);
        Assert.Single((await _git.ListStashesAsync(workspace)).Value);
        var status = (await _git.GetStatusAsync(workspace)).Value;
        Assert.False(status.IsMerging);
        Assert.Equal([new GitChange("a.yaml", GitFileStatus.Conflicted)], status.Changes);

        var conflict = await _git.GetConflictAsync(workspace, "a.yaml");
        Assert.True(conflict.IsSuccess, conflict.Error?.Message);
        Assert.Equal("name: Committed\n", conflict.Value.Ours);
        Assert.Equal("name: Stashed\n", conflict.Value.Theirs);

        var resolved = await _git.ResolveConflictAsync(workspace, "a.yaml", ConflictResolution.Theirs);
        Assert.True(resolved.IsSuccess, resolved.Error?.Message);
        Assert.Equal("name: Stashed\n", Read(workspace, "a.yaml"));
        Assert.Equal([new GitChange("a.yaml", GitFileStatus.Modified)], (await _git.GetStatusAsync(workspace)).Value.Changes);
    }

    [Fact]
    public async Task Apply_OverAnUntrackedFileOfTheSameName_IsRefused()
    {
        var (_, workspace) = InitRepositoryWithWorkspace();
        Write(workspace, "new.yaml", "name: Stashed\n");
        Assert.True((await _git.StashAsync(workspace, "New file")).IsSuccess);
        Write(workspace, "new.yaml", "name: Mine\n");
        var stash = Assert.Single((await _git.ListStashesAsync(workspace)).Value);

        var applied = await _git.ApplyStashAsync(workspace, stash, pop: true);

        Assert.Equal(GitErrors.UncommittedChangesCode, applied.Error?.Code);
        Assert.Contains("new.yaml", applied.Error!.Message);
        Assert.Equal("name: Mine\n", Read(workspace, "new.yaml"));
        Assert.Single((await _git.ListStashesAsync(workspace)).Value);
    }

    [Fact]
    public async Task List_WithoutStashes_IsEmpty()
    {
        var (_, workspace) = InitRepositoryWithWorkspace();

        var list = await _git.ListStashesAsync(workspace);

        Assert.True(list.IsSuccess, list.Error?.Message);
        Assert.Empty(list.Value);
    }

    [Theory]
    [InlineData("On main: Login work", "main", "Login work")]
    [InlineData("WIP on feature/x: abc1234 Add users", "feature/x", "abc1234 Add users")]
    [InlineData("On main: note: with colons", "main", "note: with colons")]
    [InlineData("WIP on (no branch): abc1234 Detached", null, "abc1234 Detached")]
    [InlineData("Something else", null, "Something else")]
    public void ParseStashSubject_SplitsTheBranchFromTheMessage(string subject, string? branch, string message)
    {
        Assert.Equal((branch, message), GitCliService.ParseStashSubject(subject));
    }

    [Fact]
    public void ParseStashes_NumbersEntriesByTheirLine()
    {
        var output = "aaa\u00002026-01-02T03:04:05+00:00\u0000On main: One\nbbb\u0000\u0000WIP on dev: abc Two\n";

        var stashes = GitCliService.ParseStashes(output);

        Assert.Equal(
            [
                new GitStash(0, "aaa", "One", "main", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
                new GitStash(1, "bbb", "abc Two", "dev", null),
            ],
            stashes);
        Assert.Empty(GitCliService.ParseStashes(string.Empty));
    }

    /// <summary>A repository whose workspace is its <c>api</c> subfolder, with one commit.</summary>
    private (string Repository, string Workspace) InitRepositoryWithWorkspace()
    {
        var repository = _temp.Combine("repo");
        Directory.CreateDirectory(repository);
        TestGit.Run(repository, "init", "--quiet", "--initial-branch=main");
        TestGit.Configure(repository);
        var workspace = Path.Combine(repository, "api");
        Write(workspace, "a.yaml", "name: A\n");
        Write(repository, "README.md", "# Repo\n");
        TestGit.CommitAll(repository, "Initial");
        return (repository, workspace);
    }

    private static void Write(string folder, string relativePath, string content)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(string folder, string relativePath) => File.ReadAllText(Path.Combine(folder, relativePath));
}
