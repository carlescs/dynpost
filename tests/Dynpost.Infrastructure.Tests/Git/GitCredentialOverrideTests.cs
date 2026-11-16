using Dynpost.Domain.Git;
using Dynpost.Infrastructure.Git;

namespace Dynpost.Infrastructure.Tests.Git;

/// <summary>Runs real git to check how credentials the user entered reach it, and where they go.</summary>
public sealed class GitCredentialOverrideTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly GitCommandRunner _runner = new("git");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task GitIsGivenTheCredentialsForTheirHost_EvenOverASavedLogin()
    {
        var repository = InitRepository();
        // A saved login for the same host, as the user's own helper would return it.
        UseOnlyHelper(repository, "!f() { cat >/dev/null; echo username=old; echo password=stale; }; f");

        var output = await FillAsync(repository, Credentials("https://example.com/team/api.git"), "https", "example.com");

        Assert.Contains("username=me\n", output);
        Assert.Contains("password=p@ss w'rd$1\n", output);
        Assert.DoesNotContain("stale", output);
    }

    [Fact]
    public async Task OtherHostsKeepTheUsersOwnLogin()
    {
        var repository = InitRepository();
        UseOnlyHelper(repository, "!f() { cat >/dev/null; echo username=other; echo password=theirs; }; f");

        var output = await FillAsync(repository, Credentials("https://example.com"), "https", "elsewhere.example");

        Assert.Contains("password=theirs\n", output);
        Assert.DoesNotContain("p@ss", output);
    }

    [Fact]
    public async Task CredentialsThatWorked_AreSavedThroughTheUsersHelper()
    {
        var store = _temp.Combine("saved-credentials");
        var remote = _temp.Combine("remote.git");
        TestGit.Run(_temp.Path, "init", "--quiet", "--bare", remote);
        var repository = InitRepository();
        TestGit.Run(repository, "remote", "add", "origin", remote);
        UseOnlyHelper(repository, $"store --file={store.Replace('\\', '/')}");

        var fetch = await new GitCliService().FetchAsync(repository, Credentials("https://example.com/team/api.git"));

        Assert.True(fetch.IsSuccess, fetch.Error?.Message);
        Assert.Equal("https://me:p%40ss%20w%27rd%241@example.com\n", File.ReadAllText(store));
    }

    [Fact]
    public async Task DeletingARemoteBranch_SavesCredentialsThatWorked()
    {
        var store = _temp.Combine("saved-credentials");
        var remote = _temp.Combine("remote.git");
        TestGit.Run(_temp.Path, "init", "--quiet", "--bare", remote);
        var repository = InitRepository();
        TestGit.Configure(repository);
        File.WriteAllText(Path.Combine(repository, "a.txt"), "a");
        TestGit.CommitAll(repository, "first");
        TestGit.Run(repository, "remote", "add", "origin", remote);
        TestGit.Run(repository, "push", "--quiet", "origin", "HEAD:refs/heads/topic");
        TestGit.Run(repository, "fetch", "--quiet", "origin");
        UseOnlyHelper(repository, $"store --file={store.Replace('\\', '/')}");
        var git = new GitCliService();
        var topic = (await git.ListBranchesAsync(repository)).Value.Single(b => b.IsRemote && b.Name == "origin/topic");

        var deleted = await git.DeleteRemoteBranchAsync(repository, topic, Credentials("https://example.com"));

        Assert.True(deleted.IsSuccess, deleted.Error?.Message);
        Assert.Empty(TestGit.Run(remote, "branch", "--list", "topic").Trim());
        Assert.Equal("https://me:p%40ss%20w%27rd%241@example.com\n", File.ReadAllText(store));
    }

    [Fact]
    public async Task APushTheRemoteRefuses_StillSavesTheCredentialsItAccepted()
    {
        var store = _temp.Combine("saved-credentials");
        var remote = _temp.Combine("remote.git");
        TestGit.Run(_temp.Path, "init", "--quiet", "--bare", remote);
        var other = _temp.Combine("other");
        TestGit.Run(_temp.Path, "init", "--quiet", other);
        TestGit.Configure(other);
        File.WriteAllText(Path.Combine(other, "theirs.txt"), "theirs");
        TestGit.CommitAll(other, "theirs");
        TestGit.Run(other, "push", "--quiet", remote, "HEAD:refs/heads/main");
        var repository = InitRepository();
        TestGit.Configure(repository);
        File.WriteAllText(Path.Combine(repository, "mine.txt"), "mine");
        TestGit.CommitAll(repository, "mine");
        TestGit.Run(repository, "remote", "add", "origin", remote);
        TestGit.Run(repository, "fetch", "--quiet", "origin");
        TestGit.Run(repository, "branch", "--quiet", "--set-upstream-to=origin/main");
        UseOnlyHelper(repository, $"store --file={store.Replace('\\', '/')}");

        var push = await new GitCliService().PushAsync(repository, setUpstream: false, Credentials("https://example.com"));

        Assert.Equal(Dynpost.Application.Git.GitErrors.PushRejectedCode, push.Error?.Code);
        Assert.Equal("https://me:p%40ss%20w%27rd%241@example.com\n", File.ReadAllText(store));
    }

    private async Task<string> FillAsync(string repository, GitCredentials credentials, string protocol, string host)
    {
        var result = await _runner.RunAsync(
            repository,
            GitCredentialOverride.Arguments(["credential", "fill"], credentials),
            CancellationToken.None,
            GitCredentialOverride.Environment(credentials),
            $"protocol={protocol}\nhost={host}\n\n");
        Assert.True(result.Succeeded, result.StandardError);
        return result.StandardOutput.ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Replaces the machine's credential helpers (a CI runner's credential manager) with one, so
    /// nothing reaches or prompts outside the test.
    /// </summary>
    private static void UseOnlyHelper(string repository, string helper)
    {
        TestGit.Run(repository, "config", "--add", "credential.helper", "");
        TestGit.Run(repository, "config", "--add", "credential.helper", helper);
    }

    private string InitRepository()
    {
        var repository = _temp.Combine("repo");
        TestGit.Run(_temp.Path, "init", "--quiet", repository);
        return repository;
    }

    private static GitCredentials Credentials(string url) =>
        GitCredentials.TryCreate(url, "me", "p@ss w'rd$1", out var credentials, out var error)
            ? credentials
            : throw new ArgumentException(error);
}
