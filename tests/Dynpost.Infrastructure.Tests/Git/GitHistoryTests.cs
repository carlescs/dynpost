using Dynpost.Application.Git;
using Dynpost.Domain.Git;
using Dynpost.Infrastructure.Git;

namespace Dynpost.Infrastructure.Tests.Git;

/// <summary>Reads commit history with <see cref="GitCliService"/> from real temporary repositories.</summary>
public sealed class GitHistoryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly GitCliService _git = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Log_OfABranchWithoutCommits_IsEmpty()
    {
        var repository = InitRepository();

        var log = await _git.GetLogAsync(repository, 0, 10);

        Assert.True(log.IsSuccess, log.Error?.Message);
        Assert.Empty(log.Value);
    }

    [Fact]
    public async Task Log_ListsCommitsNewestFirst_WithAuthorAndSubject()
    {
        var repository = InitRepository();
        Write(repository, "a.yaml", "name: A\n");
        TestGit.CommitAll(repository, "Add A");
        Write(repository, "b.yaml", "name: B\n");
        TestGit.CommitAll(repository, "Add B\n\nWith a body.");

        var log = await _git.GetLogAsync(repository, 0, 10);

        Assert.True(log.IsSuccess, log.Error?.Message);
        Assert.Equal(["Add B", "Add A"], log.Value.Select(c => c.Subject));
        var newest = log.Value[0];
        Assert.Equal(TestGit.Head(repository), newest.Id);
        Assert.Equal([log.Value[1].Id], newest.ParentIds);
        Assert.Empty(log.Value[1].ParentIds);
        Assert.Equal("Test", newest.AuthorName);
        Assert.Equal("test@example.com", newest.AuthorEmail);
        Assert.InRange(newest.AuthoredAt, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Log_SkipsAndLimits()
    {
        var repository = InitRepository();
        for (var i = 1; i <= 4; i++)
        {
            Write(repository, "a.yaml", $"version: {i}\n");
            TestGit.CommitAll(repository, $"Version {i}");
        }

        var log = await _git.GetLogAsync(repository, 1, 2);

        Assert.Equal(["Version 3", "Version 2"], log.Value.Select(c => c.Subject));
    }

    [Fact]
    public async Task Log_OfASubfolderWorkspace_OnlyListsCommitsThatChangedIt()
    {
        var repository = InitRepository();
        var workspace = Path.Combine(repository, "api");
        Write(workspace, "a.yaml", "name: A\n");
        TestGit.CommitAll(repository, "Add A");
        Write(repository, "README.md", "Elsewhere\n");
        TestGit.CommitAll(repository, "Outside the workspace");
        Write(workspace, ".dynpost/history.json", "[]");
        TestGit.Run(repository, "add", "--force", "--all");
        TestGit.Run(repository, "commit", "--quiet", "--message", "Local files only");

        var log = await _git.GetLogAsync(workspace, 0, 10);

        Assert.Equal(["Add A"], log.Value.Select(c => c.Subject));
    }

    [Fact]
    public async Task Commit_ReadsTheMessageAndChangedFiles()
    {
        var repository = InitRepository();
        Write(repository, "a.yaml", "name: A\n");
        Write(repository, "old.yaml", "name: Renamed request\nurl: https://example.com/a/long/enough/url\n");
        Write(repository, "gone.yaml", "name: Gone\n");
        TestGit.CommitAll(repository, "Initial");
        Write(repository, "a.yaml", "name: A2\n");
        File.Move(Path.Combine(repository, "old.yaml"), Path.Combine(repository, "new.yaml"));
        File.Delete(Path.Combine(repository, "gone.yaml"));
        Write(repository, "users/added.yaml", "name: Added\n");
        TestGit.CommitAll(repository, "Change things\n\nFirst line.\nSecond line.");

        var commit = await _git.GetCommitAsync(repository, TestGit.Head(repository)[..10]);

        Assert.True(commit.IsSuccess, commit.Error?.Message);
        Assert.Equal(TestGit.Head(repository), commit.Value.Commit.Id);
        Assert.Equal("Change things", commit.Value.Commit.Subject);
        Assert.Equal("First line.\nSecond line.", commit.Value.Body);
        Assert.Equal(
            [
                new GitChange("a.yaml", GitFileStatus.Modified),
                new GitChange("gone.yaml", GitFileStatus.Deleted),
                new GitChange("new.yaml", GitFileStatus.Renamed, "old.yaml"),
                new GitChange("users/added.yaml", GitFileStatus.Added),
            ],
            commit.Value.Files.OrderBy(f => f.Path, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Commit_RootCommit_ListsEveryFileAsAdded()
    {
        var repository = InitRepository();
        Write(repository, "a.yaml", "name: A\n");
        TestGit.CommitAll(repository, "Initial");

        var commit = await _git.GetCommitAsync(repository, TestGit.Head(repository));

        Assert.True(commit.IsSuccess, commit.Error?.Message);
        Assert.Equal("", commit.Value.Body);
        Assert.Equal([new GitChange("a.yaml", GitFileStatus.Added)], commit.Value.Files);
    }

    [Fact]
    public async Task Commit_OfASubfolderWorkspace_ListsItsFilesRelativeToIt()
    {
        var repository = InitRepository();
        var workspace = Path.Combine(repository, "api");
        Write(workspace, "users/a.yaml", "name: A\n");
        Write(workspace, ".dynpost/history.json", "[]");
        Write(repository, "README.md", "Elsewhere\n");
        TestGit.Run(repository, "add", "--force", "--all");
        TestGit.Run(repository, "commit", "--quiet", "--message", "Initial");

        var commit = await _git.GetCommitAsync(workspace, TestGit.Head(repository));

        Assert.Equal([new GitChange("users/a.yaml", GitFileStatus.Added)], commit.Value.Files);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000")]
    [InlineData("HEAD")]
    [InlineData("--output=x")]
    public async Task Commit_Unknown_IsReported(string commitId)
    {
        var repository = InitRepository();
        Write(repository, "a.yaml", "name: A\n");
        TestGit.CommitAll(repository, "Initial");

        var commit = await _git.GetCommitAsync(repository, commitId);

        Assert.Equal(GitErrors.CommitNotFoundCode, commit.Error?.Code);
    }

    [Fact]
    public async Task ReadFile_ReturnsTheVersionInTheCommit_OrNullIfMissing()
    {
        var repository = InitRepository();
        var workspace = Path.Combine(repository, "api");
        Write(workspace, "a.yaml", "name: A\n");
        TestGit.CommitAll(repository, "Initial");
        var first = TestGit.Head(repository);
        Write(workspace, "a.yaml", "name: A2\n");
        TestGit.CommitAll(repository, "Change A");

        var before = await _git.ReadFileAtCommitAsync(workspace, first, "a.yaml");
        var after = await _git.ReadFileAtCommitAsync(workspace, TestGit.Head(repository), "a.yaml");
        var missing = await _git.ReadFileAtCommitAsync(workspace, first, "missing.yaml");
        var outside = await _git.ReadFileAtCommitAsync(workspace, first, "../a.yaml");

        Assert.Equal("name: A\n", before.Value);
        Assert.Equal("name: A2\n", after.Value);
        Assert.Null(missing.Value);
        Assert.Null(outside.Value);
    }

    private string InitRepository()
    {
        var path = _temp.Combine("repo");
        Directory.CreateDirectory(path);
        TestGit.Run(path, "init", "--quiet", "--initial-branch=main");
        TestGit.Configure(path);
        return path;
    }

    private static void Write(string folder, string relativePath, string content)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
