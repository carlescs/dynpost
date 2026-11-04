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
        Assert.Equal(PullOutcome.Updated, pull.Value);
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

        Assert.Equal(PullOutcome.Conflicted, pull.Value);
        var status = (await _git.GetStatusAsync(bob)).Value;
        Assert.True(status.IsMerging);
        Assert.Equal(new[] { new GitChange("a.yaml", GitFileStatus.Conflicted) }, status.Changes);
        Assert.Contains("<<<<<<<", File.ReadAllText(Path.Combine(bob, "a.yaml")));
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
            TestGit.Run(_temp.Path, "clone", "--quiet", remote, name);
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

    private static CommitMessage Message(string text) =>
        CommitMessage.TryCreate(text, out var message) ? message : throw new ArgumentException(text);

    private static string CommittedFiles(string repository) =>
        TestGit.Run(repository, "show", "--name-only", "--format=", "HEAD").ReplaceLineEndings("\n").TrimStart('\n');
}
