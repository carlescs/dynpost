using Dynpost.Application.Git;
using Dynpost.Infrastructure.Git;

namespace Dynpost.Infrastructure.Tests.Git;

public class GitFailureTests
{
    [Theory]
    [InlineData("fatal: could not read Username for 'https://github.com': terminal prompts disabled", GitErrors.AuthenticationFailedCode)]
    [InlineData("remote: Invalid username or password.\nfatal: Authentication failed for 'https://example.com/x.git/'", GitErrors.AuthenticationFailedCode)]
    [InlineData("git@github.com: Permission denied (publickey).\nfatal: Could not read from remote repository.", GitErrors.AuthenticationFailedCode)]
    [InlineData("Host key verification failed.\nfatal: Could not read from remote repository.", GitErrors.AuthenticationFailedCode)]
    [InlineData("fatal: unable to access 'https://example.invalid/x.git/': Could not resolve host: example.invalid", GitErrors.RemoteUnreachableCode)]
    [InlineData("ssh: connect to host example.com port 22: Connection timed out", GitErrors.RemoteUnreachableCode)]
    [InlineData("fatal: '../nowhere' does not appear to be a git repository", GitErrors.RemoteUnreachableCode)]
    [InlineData("Author identity unknown\n\n*** Please tell me who you are.", GitErrors.IdentityUnknownCode)]
    [InlineData("fatal: empty ident name (for <>) not allowed", GitErrors.IdentityUnknownCode)]
    [InlineData("fatal: refusing to merge unrelated histories", GitErrors.UnrelatedHistoriesCode)]
    [InlineData("fatal: detected dubious ownership in repository at '/w'\nTo add an exception for this directory, call:\n\n\tgit config --global --add safe.directory /w", GitErrors.UnsafeRepositoryCode)]
    [InlineData("fatal: Unable to create '/w/.git/index.lock': File exists.\n\nAnother git process seems to be running", GitErrors.RepositoryLockedCode)]
    [InlineData("fatal: destination path '/w/api' already exists and is not an empty directory.", GitErrors.CloneDestinationExistsCode)]
    [InlineData("fatal: 'feat' is already used by worktree at '/w/api-feat'", GitErrors.BranchInWorktreeCode)]
    [InlineData("fatal: 'feat' is already checked out at '/w/api-feat'", GitErrors.BranchInWorktreeCode)]
    [InlineData("error: cannot delete branch 'feat' used by worktree at '/w/api-feat'", GitErrors.BranchInWorktreeCode)]
    [InlineData("error: something else entirely", GitErrors.CommandFailedCode)]
    public void Classify_RecognizesCommonFailures(string details, string code)
    {
        Assert.Equal(code, GitFailure.Classify("pull", details).Code);
    }

    [Theory]
    [InlineData("fatal: could not read Username for 'https://github.com': terminal prompts disabled", "https://github.com")]
    [InlineData("remote: Invalid username or password.\nfatal: Authentication failed for 'https://me@git.example.com:8443/team/x.git/'", "https://me@git.example.com:8443")]
    [InlineData("fatal: unable to access 'http://git.local/x.git/': The requested URL returned error: 403", "http://git.local")]
    [InlineData("git@github.com: Permission denied (publickey).\nfatal: Could not read from remote repository.", null)]
    public void AuthenticationFailed_NamesTheHttpUrlToSignInTo(string details, string? url)
    {
        var error = GitFailure.Classify("pull", details);

        Assert.Equal(GitErrors.AuthenticationFailedCode, error.Code);
        Assert.Equal(url, error.Target);
    }

    [Fact]
    public void UnsafeRepository_ShowsTheCommandThatTrustsIt()
    {
        var error = GitFailure.Classify(
            "status",
            "fatal: detected dubious ownership in repository at '/w'\nTo add an exception for this directory, call:\n\n\tgit config --global --add safe.directory /w\n");

        Assert.EndsWith("git config --global --add safe.directory /w", error.Message);
    }

    [Fact]
    public void OverwrittenChanges_ListTheFiles()
    {
        const string details = """
            error: Your local changes to the following files would be overwritten by merge:
            	api/a.yaml
            	b.yaml
            Please commit your changes or stash them before you merge.
            error: The following untracked working tree files would be overwritten by merge:
            	c.yaml
            Please move or remove them before you merge.
            Aborting
            """;

        var error = GitFailure.Classify("pull", details);

        Assert.Equal(GitErrors.LocalChangesOverwrittenCode, error.Code);
        Assert.Equal(["api/a.yaml", "b.yaml", "c.yaml"], GitFailure.OverwrittenPaths(details));
        Assert.Contains("api/a.yaml, b.yaml, c.yaml", error.Message);
    }

    [Fact]
    public void BranchInWorktree_NamesTheBranchAndTheWorktree()
    {
        var error = GitFailure.Classify("switch", "fatal: 'feat' is already used by worktree at '/w/api-feat'");

        Assert.Contains("'feat' is checked out in the worktree at '/w/api-feat'", error.Message);
    }
}
