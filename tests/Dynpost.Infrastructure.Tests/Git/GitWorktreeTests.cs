using Dynpost.Application.Git;
using Dynpost.Domain.Git;
using Dynpost.Infrastructure.Git;

namespace Dynpost.Infrastructure.Tests.Git;

/// <summary>Manages worktrees with <see cref="GitCliService"/> in real temporary repositories.</summary>
public sealed class GitWorktreeTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly GitCliService _git = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Worktrees_AreAddedListedAndRemoved()
    {
        var (repository, workspace) = InitRepositoryWithWorkspace();
        TestGit.Run(repository, "branch", "feature");
        var folder = _temp.Combine("repo-feature");

        var added = await _git.AddWorktreeAsync(workspace, folder, "feature", createBranch: false);

        Assert.True(added.IsSuccess, added.Error?.Message);
        Assert.Equal("name: A\n", File.ReadAllText(Path.Combine(folder, "api", "a.yaml")));
        var list = await _git.ListWorktreesAsync(workspace);
        Assert.True(list.IsSuccess, list.Error?.Message);
        Assert.Equal(2, list.Value.Count);
        var (main, linked) = (list.Value[0], list.Value[1]);
        Assert.True(main.IsMain);
        Assert.True(main.IsCurrent);
        Assert.Equal("main", main.Branch);
        Assert.False(linked.IsMain);
        Assert.False(linked.IsCurrent);
        Assert.Equal("feature", linked.Branch);
        Assert.Equal(TestGit.Head(repository), linked.Head);
        Assert.Equal(Path.Combine(linked.Path, "api"), linked.WorkspaceFolder);
        Assert.True(Directory.Exists(linked.WorkspaceFolder));

        // Seen from the linked worktree, it is the current one.
        var fromLinked = await _git.ListWorktreesAsync(linked.WorkspaceFolder);
        Assert.True(fromLinked.Value.Single(w => w.Branch == "feature").IsCurrent);
        Assert.False(fromLinked.Value.Single(w => w.Branch == "main").IsCurrent);

        // Ignored local files don't count as changes.
        Directory.CreateDirectory(Path.Combine(linked.WorkspaceFolder, ".dynpost"));
        File.WriteAllText(Path.Combine(linked.WorkspaceFolder, ".dynpost", "secrets.yaml"), "x: y\n");
        var removed = await _git.RemoveWorktreeAsync(workspace, linked, force: false);

        Assert.True(removed.IsSuccess, removed.Error?.Message);
        Assert.False(Directory.Exists(folder));
        Assert.Single((await _git.ListWorktreesAsync(workspace)).Value);
        Assert.Contains("feature", TestGit.Run(repository, "branch", "--list", "feature"));
    }

    [Fact]
    public async Task AddWithANewBranch_CreatesItAtTheCurrentCommit()
    {
        var (repository, workspace) = InitRepositoryWithWorkspace();

        var added = await _git.AddWorktreeAsync(workspace, _temp.Combine("repo-login"), "login", createBranch: true);

        Assert.True(added.IsSuccess, added.Error?.Message);
        var linked = (await _git.ListWorktreesAsync(workspace)).Value.Single(w => w.Branch == "login");
        Assert.Equal(TestGit.Head(repository), linked.Head);
    }

    [Fact]
    public async Task ABranchCheckedOutInAnotherWorktree_IsReported()
    {
        var (repository, workspace) = InitRepositoryWithWorkspace();
        TestGit.Run(repository, "branch", "feature");
        Assert.True((await _git.AddWorktreeAsync(workspace, _temp.Combine("repo-feature"), "feature", false)).IsSuccess);

        var again = await _git.AddWorktreeAsync(workspace, _temp.Combine("repo-feature-2"), "feature", false);
        var current = await _git.AddWorktreeAsync(workspace, _temp.Combine("repo-main"), "main", false);
        var switched = await _git.SwitchBranchAsync(workspace, "feature");
        var deleted = await _git.DeleteBranchAsync(workspace, "feature", force: true);

        Assert.Equal(GitErrors.BranchInWorktreeCode, again.Error?.Code);
        Assert.Contains("repo-feature", again.Error!.Message);
        Assert.Equal(GitErrors.BranchInWorktreeCode, current.Error?.Code);
        Assert.Equal(GitErrors.BranchInWorktreeCode, switched.Error?.Code);
        Assert.Equal(GitErrors.BranchInWorktreeCode, deleted.Error?.Code);
    }

    [Fact]
    public async Task AddIntoAnExistingFolder_IsReported()
    {
        var (_, workspace) = InitRepositoryWithWorkspace();
        var folder = _temp.Combine("taken");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "file.txt"), "mine\n");

        var added = await _git.AddWorktreeAsync(workspace, folder, "other", createBranch: true);

        Assert.Equal(GitErrors.WorktreeFolderExistsCode, added.Error?.Code);
        Assert.Equal("mine\n", File.ReadAllText(Path.Combine(folder, "file.txt")));
    }

    [Fact]
    public async Task RemovingAWorktreeWithNewFiles_NeedsForce()
    {
        var (_, workspace) = InitRepositoryWithWorkspace();
        Assert.True((await _git.AddWorktreeAsync(workspace, _temp.Combine("repo-x"), "x", true)).IsSuccess);
        var linked = (await _git.ListWorktreesAsync(workspace)).Value.Single(w => w.Branch == "x");
        File.WriteAllText(Path.Combine(linked.WorkspaceFolder, "new.yaml"), "name: New\n");

        var kept = await _git.RemoveWorktreeAsync(workspace, linked, force: false);
        Assert.Equal(GitErrors.WorktreeHasChangesCode, kept.Error?.Code);
        Assert.True(File.Exists(Path.Combine(linked.WorkspaceFolder, "new.yaml")));

        var forced = await _git.RemoveWorktreeAsync(workspace, linked, force: true);
        Assert.True(forced.IsSuccess, forced.Error?.Message);
        Assert.False(Directory.Exists(linked.Path));
    }

    [Fact]
    public async Task ALockedWorktree_IsReportedAsLocked_AndNotRemovedEvenWithForce()
    {
        var (repository, workspace) = InitRepositoryWithWorkspace();
        Assert.True((await _git.AddWorktreeAsync(workspace, _temp.Combine("repo-x"), "x", true)).IsSuccess);
        TestGit.Run(repository, "worktree", "lock", _temp.Combine("repo-x"));
        var linked = (await _git.ListWorktreesAsync(workspace)).Value.Single(w => w.Branch == "x");
        Assert.True(linked.IsLocked);

        var removed = await _git.RemoveWorktreeAsync(workspace, linked, force: true);

        Assert.Equal(GitErrors.WorktreeLockedCode, removed.Error?.Code);
        Assert.True(Directory.Exists(linked.Path));
    }

    [Fact]
    public async Task AWorktreeDeletedOnDisk_IsPrunable_AndPruned()
    {
        var (_, workspace) = InitRepositoryWithWorkspace();
        var folder = _temp.Combine("repo-x");
        Assert.True((await _git.AddWorktreeAsync(workspace, folder, "x", true)).IsSuccess);
        Directory.Delete(folder, recursive: true);

        var linked = (await _git.ListWorktreesAsync(workspace)).Value.Single(w => w.Branch == "x");
        Assert.True(linked.IsPrunable);

        Assert.True((await _git.PruneWorktreesAsync(workspace)).IsSuccess);
        Assert.Single((await _git.ListWorktreesAsync(workspace)).Value);
    }

    [Fact]
    public void ParseWorktrees_ReadsEachRecord_AndSkipsABareRepository()
    {
        var output = string.Join('\0',
            "worktree /r/bare.git", "bare", "",
            "worktree /r/main", "HEAD abc", "branch refs/heads/main", "",
            "worktree /r/detached", "HEAD def", "detached", "locked reason here", "",
            "worktree /r/gone", "HEAD 123", "branch refs/heads/old", "prunable gitdir file points to non-existent location", "") + "\0";

        var worktrees = GitCliService.ParseWorktrees(output, "/r/detached/", "api/");

        Assert.Equal(["/r/main", "/r/detached", "/r/gone"], worktrees.Select(w => w.Path));
        Assert.False(worktrees[0].IsMain); // The bare repository was the main entry.
        Assert.Equal("main", worktrees[0].Branch);
        Assert.Equal(Path.Combine("/r/main", "api"), worktrees[0].WorkspaceFolder);
        Assert.True(worktrees[1].IsCurrent);
        Assert.True(worktrees[1].IsDetached);
        Assert.True(worktrees[1].IsLocked);
        Assert.Equal("def", worktrees[1].Head);
        Assert.True(worktrees[2].IsPrunable);
        Assert.False(worktrees[2].IsLocked);
    }

    /// <summary>A repository whose workspace is its <c>api</c> subfolder, with one commit.</summary>
    private (string Repository, string Workspace) InitRepositoryWithWorkspace()
    {
        var repository = _temp.Combine("repo");
        Directory.CreateDirectory(repository);
        TestGit.Run(repository, "init", "--quiet", "--initial-branch=main");
        TestGit.Configure(repository);
        var workspace = Path.Combine(repository, "api");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "a.yaml"), "name: A\n");
        File.WriteAllText(Path.Combine(workspace, ".gitignore"), ".dynpost/\n");
        TestGit.CommitAll(repository, "Initial");
        return (repository, workspace);
    }
}
