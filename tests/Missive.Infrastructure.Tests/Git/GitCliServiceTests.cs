using Missive.Application.Git;
using Missive.Domain.Git;
using Missive.Infrastructure.Git;

namespace Missive.Infrastructure.Tests.Git;

/// <summary>Runs <see cref="GitCliService"/> against real temporary repositories.</summary>
public sealed class GitCliServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly GitCliService _git = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task MissingFolder_IsReported()
    {
        var result = await _git.GetStatusAsync(_temp.Combine("missing"));

        Assert.Equal(GitErrors.WorkspaceNotFoundCode, result.Error?.Code);
    }

    [Fact]
    public async Task FolderOutsideARepository_IsReported()
    {
        var result = await _git.GetStatusAsync(_temp.Path);

        Assert.Equal(GitErrors.NotARepositoryCode, result.Error?.Code);
    }

    [Fact]
    public async Task Init_CreatesARepositoryThatIgnoresTheLocalFolder()
    {
        var workspace = _temp.Combine("new");
        Write(workspace, "health.yaml", "name: Health\n");
        Write(workspace, ".missive/secrets.yaml", "apiToken: secret\n");

        var result = await _git.InitAsync(workspace);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(Directory.Exists(Path.Combine(workspace, ".git")));
        Assert.Equal(".missive/\n", File.ReadAllText(Path.Combine(workspace, ".gitignore")));
        var status = await _git.GetStatusAsync(workspace);
        Assert.True(status.IsSuccess, status.Error?.Message);
        Assert.Equal([".gitignore", "health.yaml"], status.Value.Changes.Select(c => c.Path).Order());
    }

    [Fact]
    public async Task Init_AppendsToAnExistingGitIgnore()
    {
        var workspace = _temp.Combine("new");
        Write(workspace, ".gitignore", "*.log");

        var result = await _git.InitAsync(workspace);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("*.log\n.missive/\n", File.ReadAllText(Path.Combine(workspace, ".gitignore")));
    }

    [Fact]
    public async Task Init_LeavesAGitIgnoreThatAlreadyIgnoresTheLocalFolder()
    {
        var workspace = _temp.Combine("new");
        Write(workspace, ".gitignore", "/.missive\r\nbin/\r\n");

        var result = await _git.InitAsync(workspace);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("/.missive\r\nbin/\r\n", File.ReadAllText(Path.Combine(workspace, ".gitignore")));
    }

    [Fact]
    public async Task Init_InsideARepository_IsRefused()
    {
        var repository = InitRepository("repo");
        var workspace = Path.Combine(repository, "api");
        Directory.CreateDirectory(workspace);

        var result = await _git.InitAsync(workspace);

        Assert.Equal(GitErrors.AlreadyARepositoryCode, result.Error?.Code);
        Assert.False(Directory.Exists(Path.Combine(workspace, ".git")));
        Assert.False(File.Exists(Path.Combine(workspace, ".gitignore")));
    }

    [Fact]
    public async Task Init_MissingFolder_IsReported()
    {
        var result = await _git.InitAsync(_temp.Combine("missing"));

        Assert.Equal(GitErrors.WorkspaceNotFoundCode, result.Error?.Code);
    }

    [Fact]
    public async Task MissingGitExecutable_IsReported()
    {
        var git = new GitCliService("missive-no-such-git-executable");

        var result = await git.GetStatusAsync(_temp.Path);

        Assert.Equal(GitErrors.GitNotInstalledCode, result.Error?.Code);
    }

    [Fact]
    public async Task Status_ListsWorkspaceChangesRelativeToTheWorkspace()
    {
        var repository = InitRepository("repo");
        var workspace = Path.Combine(repository, "api");
        Write(workspace, "health.yaml", "name: Health\n");
        Write(workspace, "old.yaml", "name: Old\n");
        Write(repository, "README.md", "# Repo\n");
        TestGit.CommitAll(repository, "Initial");

        Write(workspace, "health.yaml", "name: Health check\n");
        File.Delete(Path.Combine(workspace, "old.yaml"));
        Write(workspace, "users/get user.yaml", "name: Get user\n");
        Write(workspace, ".missive/secrets.yaml", "apiToken: secret\n");
        Write(repository, "README.md", "# Changed outside the workspace\n");

        var result = await _git.GetStatusAsync(workspace);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var status = result.Value;
        Assert.Equal("main", status.Branch);
        Assert.Null(status.Upstream);
        Assert.False(status.IsMerging);
        Assert.Equal(
            new[]
            {
                new GitChange("health.yaml", GitFileStatus.Modified),
                new GitChange("old.yaml", GitFileStatus.Deleted),
                new GitChange("users/get user.yaml", GitFileStatus.Untracked),
            },
            status.Changes);
    }

    [Fact]
    public async Task Commit_CommitsEveryWorkspaceChange_ButNotLocalOrOutsideFiles()
    {
        var repository = InitRepository("repo");
        var workspace = Path.Combine(repository, "api");
        Write(workspace, "users/create-user.yaml", "name: Create user\n");
        Write(workspace, ".missive/secrets.yaml", "apiToken: secret\n");
        Write(repository, "outside.txt", "not part of the workspace\n");

        var result = await _git.CommitAsync(workspace, Message("Add create user"), paths: null);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(TestGit.Head(repository), result.Value);
        Assert.Equal("api/users/create-user.yaml\n", CommittedFiles(repository));
        Assert.Equal("Add create user", TestGit.Run(repository, "log", "-1", "--format=%B").Trim());
        var status = await _git.GetStatusAsync(workspace);
        Assert.Empty(status.Value.Changes);
    }

    [Fact]
    public async Task Commit_WithPaths_CommitsOnlyThosePaths()
    {
        var repository = InitRepository("repo");
        Write(repository, "a.yaml", "name: A\n");
        Write(repository, "b.yaml", "name: B\n");

        var result = await _git.CommitAsync(repository, Message("Add A"), ["a.yaml"]);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("a.yaml\n", CommittedFiles(repository));
        var status = await _git.GetStatusAsync(repository);
        Assert.Equal(new[] { new GitChange("b.yaml", GitFileStatus.Untracked) }, status.Value.Changes);
    }

    [Fact]
    public async Task Push_WithSetUpstream_PublishesTheBranch()
    {
        var remote = InitBareRemote();
        var repository = InitRepository("repo");
        TestGit.Run(repository, "remote", "add", "origin", remote);
        Write(repository, "a.yaml", "name: A\n");
        TestGit.CommitAll(repository, "Add A");

        var push = await _git.PushAsync(repository, setUpstream: true);

        Assert.True(push.IsSuccess, push.Error?.Message);
        var status = await _git.GetStatusAsync(repository);
        Assert.Equal("origin/main", status.Value.Upstream);
        Assert.Equal(0, status.Value.Ahead);
        Assert.Equal(TestGit.Head(repository), TestGit.Run(remote, "rev-parse", "main").Trim());
    }

    [Fact]
    public async Task PushThenPull_SyncsTwoClones()
    {
        var (alice, bob) = InitTwoClones();
        Write(alice, "a.yaml", "name: A changed\n");
        TestGit.CommitAll(alice, "Change A");

        var push = await _git.PushAsync(alice, setUpstream: false);
        var pull = await _git.PullAsync(bob);
        var again = await _git.PullAsync(bob);

        Assert.True(push.IsSuccess, push.Error?.Message);
        Assert.True(pull.IsSuccess, pull.Error?.Message);
        Assert.Equal(PullOutcome.Updated, pull.Value);
        Assert.True(again.IsSuccess, again.Error?.Message);
        Assert.Equal(PullOutcome.UpToDate, again.Value);
        Assert.Equal("name: A changed\n", File.ReadAllText(Path.Combine(bob, "a.yaml")));
    }

    [Fact]
    public async Task Push_BehindTheRemote_IsRejected()
    {
        var (alice, bob) = InitTwoClones();
        Write(alice, "a.yaml", "name: From Alice\n");
        TestGit.CommitAll(alice, "Alice");
        TestGit.Run(alice, "push", "--quiet");
        Write(bob, "b.yaml", "name: From Bob\n");
        TestGit.CommitAll(bob, "Bob");

        var push = await _git.PushAsync(bob, setUpstream: false);

        Assert.Equal(GitErrors.PushRejectedCode, push.Error?.Code);
    }

    [Fact]
    public async Task Pull_WithConflictingChanges_LeavesTheConflictForTheUser()
    {
        var (alice, bob) = InitTwoClones();
        Write(alice, "a.yaml", "name: From Alice\n");
        TestGit.CommitAll(alice, "Alice");
        TestGit.Run(alice, "push", "--quiet");
        Write(bob, "a.yaml", "name: From Bob\n");
        TestGit.CommitAll(bob, "Bob");

        var pull = await _git.PullAsync(bob);

        Assert.True(pull.IsSuccess, pull.Error?.Message);
        Assert.Equal(PullOutcome.Conflicted, pull.Value);
        var status = (await _git.GetStatusAsync(bob)).Value;
        Assert.True(status.IsMerging);
        Assert.Equal(new[] { new GitChange("a.yaml", GitFileStatus.Conflicted) }, status.Changes);
        Assert.Contains("<<<<<<<", File.ReadAllText(Path.Combine(bob, "a.yaml")));
    }

    [Fact]
    public async Task Conflict_ReportsAllVersionsAndTheMergeMessage()
    {
        var bob = await ConflictedMergeAsync();

        var status = (await _git.GetStatusAsync(bob)).Value;
        var conflict = await _git.GetConflictAsync(bob, "a.yaml");

        Assert.StartsWith("Merge branch", status.MergeMessage);
        Assert.DoesNotContain("#", status.MergeMessage);
        Assert.True(conflict.IsSuccess, conflict.Error?.Message);
        Assert.Equal("name: A\n", conflict.Value.Base);
        Assert.Equal("name: From Bob\n", conflict.Value.Ours);
        Assert.Equal("name: From Alice\n", conflict.Value.Theirs);
        Assert.True(conflict.Value.HasConflictMarkers);

        var deletedByThem = (await _git.GetConflictAsync(bob, "d.yaml")).Value;
        Assert.Equal("name: Changed by Bob\n", deletedByThem.Ours);
        Assert.Null(deletedByThem.Theirs);
    }

    [Fact]
    public async Task ResolvingEveryFile_ThenCommitting_CompletesTheMerge()
    {
        var bob = await ConflictedMergeAsync();

        var theirs = await _git.ResolveConflictAsync(bob, "a.yaml", ConflictResolution.Theirs);
        var deleted = await _git.ResolveConflictAsync(bob, "d.yaml", ConflictResolution.Theirs);

        Assert.True(theirs.IsSuccess, theirs.Error?.Message);
        Assert.True(deleted.IsSuccess, deleted.Error?.Message);
        Assert.Equal("name: From Alice\n", File.ReadAllText(Path.Combine(bob, "a.yaml")));
        Assert.False(File.Exists(Path.Combine(bob, "d.yaml")));
        var status = (await _git.GetStatusAsync(bob)).Value;
        Assert.True(status.IsMerging);
        Assert.False(status.HasConflicts);

        var commit = await _git.CommitAsync(bob, Message(status.MergeMessage!), paths: null);

        Assert.True(commit.IsSuccess, commit.Error?.Message);
        var after = (await _git.GetStatusAsync(bob)).Value;
        Assert.False(after.IsMerging);
        Assert.Equal(2, TestGit.Run(bob, "rev-list", "--parents", "-n", "1", "HEAD").Trim().Split(' ').Length - 1);
    }

    [Fact]
    public async Task Resolve_Ours_And_Edited()
    {
        var bob = await ConflictedMergeAsync();
        Write(bob, "a.yaml", "name: From Bob and Alice\n");

        var edited = await _git.ResolveConflictAsync(bob, "a.yaml", ConflictResolution.Edited);
        var ours = await _git.ResolveConflictAsync(bob, "d.yaml", ConflictResolution.Ours);

        Assert.True(edited.IsSuccess, edited.Error?.Message);
        Assert.True(ours.IsSuccess, ours.Error?.Message);
        Assert.Equal("name: Changed by Bob\n", File.ReadAllText(Path.Combine(bob, "d.yaml")));
        Assert.Equal("name: From Bob and Alice\n", TestGit.Run(bob, "show", ":a.yaml"));
        Assert.False((await _git.GetStatusAsync(bob)).Value.HasConflicts);
    }

    [Fact]
    public async Task AbortMerge_RestoresTheBranch()
    {
        var bob = await ConflictedMergeAsync();

        var abort = await _git.AbortMergeAsync(bob);

        Assert.True(abort.IsSuccess, abort.Error?.Message);
        var status = (await _git.GetStatusAsync(bob)).Value;
        Assert.False(status.IsMerging);
        Assert.Empty(status.Changes);
        Assert.Null(status.MergeMessage);
        Assert.Equal("name: From Bob\n", File.ReadAllText(Path.Combine(bob, "a.yaml")));
    }

    /// <summary>
    /// Bob's clone after a pull that conflicts on <c>a.yaml</c> (both changed it) and
    /// <c>d.yaml</c> (Alice deleted it, Bob changed it).
    /// </summary>
    [Fact]
    public async Task Diff_ReadsTheCommittedAndWorkingVersions()
    {
        var repository = InitRepository("repo");
        var workspace = Path.Combine(repository, "api");
        Write(workspace, "users/get.yaml", "name: Get\n");
        Write(workspace, "old.yaml", "name: Old\n");
        TestGit.CommitAll(repository, "Initial");
        Write(workspace, "users/get.yaml", "name: Get user\n");
        Write(workspace, "new.yaml", "name: New\n");
        File.Delete(Path.Combine(workspace, "old.yaml"));

        var modified = await _git.GetFileDiffAsync(workspace, new GitChange("users/get.yaml", GitFileStatus.Modified));
        var added = await _git.GetFileDiffAsync(workspace, new GitChange("new.yaml", GitFileStatus.Untracked));
        var deleted = await _git.GetFileDiffAsync(workspace, new GitChange("old.yaml", GitFileStatus.Deleted));

        Assert.True(modified.IsSuccess, modified.Error?.Message);
        Assert.Equal("name: Get\n", modified.Value.Original);
        Assert.Equal("name: Get user\n", modified.Value.Modified);
        Assert.Null(added.Value.Original);
        Assert.Equal("name: New\n", added.Value.Modified);
        Assert.Equal("name: Old\n", deleted.Value.Original);
        Assert.Null(deleted.Value.Modified);
    }

    [Fact]
    public async Task Diff_OfARename_ReadsTheOriginalAtItsOldPath()
    {
        var workspace = InitRepository("repo");
        Write(workspace, "a.yaml", "name: A\n");
        TestGit.CommitAll(workspace, "Initial");
        TestGit.Run(workspace, "mv", "a.yaml", "b.yaml");
        Write(workspace, "b.yaml", "name: B\n");

        var diff = await _git.GetFileDiffAsync(workspace, new GitChange("b.yaml", GitFileStatus.Renamed, "a.yaml"));

        Assert.True(diff.IsSuccess, diff.Error?.Message);
        Assert.Equal("name: A\n", diff.Value.Original);
        Assert.Equal("name: B\n", diff.Value.Modified);
    }

    [Fact]
    public async Task Diff_BeforeTheFirstCommit_HasNoOriginal()
    {
        var workspace = InitRepository("repo");
        Write(workspace, "a.yaml", "name: A\n");

        var diff = await _git.GetFileDiffAsync(workspace, new GitChange("a.yaml", GitFileStatus.Modified));

        Assert.True(diff.IsSuccess, diff.Error?.Message);
        Assert.Null(diff.Value.Original);
        Assert.Equal("name: A\n", diff.Value.Modified);
    }

    [Fact]
    public async Task Diff_OfABinaryFile_HasNoText()
    {
        var workspace = InitRepository("repo");
        File.WriteAllBytes(Path.Combine(workspace, "logo.png"), [0x89, 0x50, 0x00, 0x01]);

        var diff = await _git.GetFileDiffAsync(workspace, new GitChange("logo.png", GitFileStatus.Untracked));

        Assert.True(diff.Value.IsBinary);
        Assert.Null(diff.Value.Modified);
    }

    [Fact]
    public async Task Diff_OfAPathOutsideTheWorkspace_IsRefused()
    {
        var workspace = InitRepository("repo");

        var diff = await _git.GetFileDiffAsync(workspace, new GitChange("../secret.txt", GitFileStatus.Modified));

        Assert.Equal(GitErrors.FileNotChangedCode, diff.Error?.Code);
    }

    [Fact]
    public async Task Diff_OfAFileRemovedFromTheIndexButKept_ComparesBothVersions()
    {
        var workspace = InitRepository("repo");
        Write(workspace, "a.yaml", "name: A\n");
        TestGit.CommitAll(workspace, "Initial");
        TestGit.Run(workspace, "rm", "--cached", "--quiet", "a.yaml");
        Write(workspace, "a.yaml", "name: A2\n");

        var deleted = await _git.GetFileDiffAsync(workspace, new GitChange("a.yaml", GitFileStatus.Deleted));
        var untracked = await _git.GetFileDiffAsync(workspace, new GitChange("a.yaml", GitFileStatus.Untracked));

        Assert.Equal(("name: A\n", "name: A2\n"), (deleted.Value.Original, deleted.Value.Modified));
        Assert.Equal(("name: A\n", "name: A2\n"), (untracked.Value.Original, untracked.Value.Modified));
    }

    [Fact]
    public async Task Diff_OfASymbolicLink_ShowsItsTarget_WithoutFollowingIt()
    {
        var workspace = InitRepository("repo");
        var secret = _temp.Combine("secret.txt");
        File.WriteAllText(secret, "password\n");
        if (!TryCreateSymbolicLink(Path.Combine(workspace, "link.yaml"), secret))
        {
            return; // Creating links needs extra rights on Windows.
        }

        var diff = await _git.GetFileDiffAsync(workspace, new GitChange("link.yaml", GitFileStatus.Untracked));

        Assert.True(diff.IsSuccess, diff.Error?.Message);
        Assert.Equal(secret, diff.Value.Modified);
    }

    [Fact]
    public async Task Diff_ThroughALinkedFolder_IsRefused()
    {
        var workspace = InitRepository("repo");
        var outside = _temp.Combine("outside");
        Write(outside, "secret.yaml", "password\n");
        if (!TryCreateSymbolicLink(Path.Combine(workspace, "linked"), outside))
        {
            return; // Creating links needs extra rights on Windows.
        }

        var diff = await _git.GetFileDiffAsync(workspace, new GitChange("linked/secret.yaml", GitFileStatus.Untracked));

        Assert.Equal(GitErrors.CommandFailedCode, diff.Error?.Code);
        Assert.DoesNotContain("password", diff.Error!.Message);
    }

    [Fact]
    public async Task Remotes_CanBeAddedRenamedChangedAndRemoved()
    {
        var workspace = InitRepository("repo");
        Assert.Empty((await _git.ListRemotesAsync(workspace)).Value);

        Assert.True((await _git.AddRemoteAsync(workspace, Name("origin"), Url("https://example.com/a.git"))).IsSuccess);
        Assert.True((await _git.AddRemoteAsync(workspace, Name("backup"), Url("git@example.com:b.git"))).IsSuccess);
        TestGit.Run(workspace, "remote", "set-url", "--push", "backup", "ssh://example.com/push.git");

        var remotes = (await _git.ListRemotesAsync(workspace)).Value;
        Assert.Equal(
            [
                new GitRemote("backup", "git@example.com:b.git", "ssh://example.com/push.git"),
                new GitRemote("origin", "https://example.com/a.git", "https://example.com/a.git"),
            ],
            remotes);

        Assert.True((await _git.RenameRemoteAsync(workspace, "origin", Name("upstream"))).IsSuccess);
        Assert.True((await _git.SetRemoteUrlAsync(workspace, "upstream", Url("https://example.com/c.git"))).IsSuccess);
        Assert.True((await _git.RemoveRemoteAsync(workspace, "backup")).IsSuccess);

        remotes = (await _git.ListRemotesAsync(workspace)).Value;
        Assert.Equal([new GitRemote("upstream", "https://example.com/c.git", "https://example.com/c.git")], remotes);
    }

    [Fact]
    public async Task Remotes_GitFailures_AreReported()
    {
        var workspace = InitRepository("repo");

        var result = await _git.RemoveRemoteAsync(workspace, "missing");

        Assert.Equal(GitErrors.CommandFailedCode, result.Error?.Code);
    }

    [Fact]
    public void ParseRemotes_ReadsFetchAndPushUrls()
    {
        var remotes = GitCliService.ParseRemotes(
            "origin\thttps://a (fetch)\r\norigin\thttps://a (push)\nfork\t/path with spaces (fetch)\nfork\tssh://p (push)\n");

        Assert.Equal(
            [new GitRemote("fork", "/path with spaces", "ssh://p"), new GitRemote("origin", "https://a", "https://a")],
            remotes);
    }

    private async Task<string> ConflictedMergeAsync()
    {
        var (alice, bob) = InitTwoClones();
        Write(alice, "d.yaml", "name: D\n");
        TestGit.CommitAll(alice, "Add D");
        TestGit.Run(alice, "push", "--quiet");
        TestGit.Run(bob, "pull", "--quiet", "--no-rebase");

        Write(alice, "a.yaml", "name: From Alice\n");
        File.Delete(Path.Combine(alice, "d.yaml"));
        TestGit.CommitAll(alice, "Alice");
        TestGit.Run(alice, "push", "--quiet");
        Write(bob, "a.yaml", "name: From Bob\n");
        Write(bob, "d.yaml", "name: Changed by Bob\n");
        TestGit.CommitAll(bob, "Bob");

        var pull = await _git.PullAsync(bob);
        Assert.Equal(PullOutcome.Conflicted, pull.Value);
        return bob;
    }

    private string InitRepository(string name)
    {
        var path = _temp.Combine(name);
        Directory.CreateDirectory(path);
        TestGit.Run(path, "init", "--quiet", "--initial-branch=main");
        TestGit.Configure(path);
        return path;
    }

    private string InitBareRemote()
    {
        var path = _temp.Combine("remote.git");
        Directory.CreateDirectory(path);
        TestGit.Run(path, "init", "--quiet", "--bare", "--initial-branch=main");
        return path;
    }

    /// <summary>A remote with one commit on <c>main</c>, cloned twice.</summary>
    private (string Alice, string Bob) InitTwoClones()
    {
        var remote = InitBareRemote();
        var seed = InitRepository("seed");
        Write(seed, "a.yaml", "name: A\n");
        TestGit.CommitAll(seed, "Add A");
        TestGit.Run(seed, "push", "--quiet", remote, "main");

        string Clone(string name)
        {
            // Set before checkout, so a global core.autocrlf=true (Windows runners) can't rewrite
            // line endings and make the clone look modified.
            TestGit.Run(_temp.Path, "clone", "--quiet", "--config", "core.autocrlf=false", remote, name);
            var path = _temp.Combine(name);
            TestGit.Configure(path, name);
            return path;
        }

        return (Clone("alice"), Clone("bob"));
    }

    private static void Write(string folder, string relativePath, string content)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static bool TryCreateSymbolicLink(string path, string target)
    {
        try
        {
            if (Directory.Exists(target))
            {
                Directory.CreateSymbolicLink(path, target);
            }
            else
            {
                File.CreateSymbolicLink(path, target);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static RemoteName Name(string text) =>
        RemoteName.TryCreate(text, out var name, out var error) ? name : throw new ArgumentException(error);

    private static RemoteUrl Url(string text) =>
        RemoteUrl.TryCreate(text, out var url, out var error) ? url : throw new ArgumentException(error);

    private static CommitMessage Message(string text) =>
        CommitMessage.TryCreate(text, out var message) ? message : throw new ArgumentException(text);

    private static string CommittedFiles(string repository) =>
        TestGit.Run(repository, "show", "--name-only", "--format=", "HEAD").ReplaceLineEndings("\n").TrimStart('\n');
}
