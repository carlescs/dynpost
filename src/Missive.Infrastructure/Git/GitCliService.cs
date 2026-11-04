using Missive.Application.Common;
using Missive.Application.Git;
using Missive.Domain.Git;

namespace Missive.Infrastructure.Git;

/// <summary>
/// <see cref="IGitService"/> backed by the git command-line client, run in the workspace folder.
/// </summary>
public sealed class GitCliService : IGitService
{
    /// <summary>Keeps the workspace's local-only folder out of status and commits, even if it is not git-ignored.</summary>
    private const string ExcludeLocalFolder = ":(exclude).missive";

    private readonly GitCommandRunner _runner;

    public GitCliService()
        : this("git")
    {
    }

    /// <param name="gitExecutable">The git executable to run: a name on the PATH or a full path.</param>
    public GitCliService(string gitExecutable)
    {
        _runner = new GitCommandRunner(gitExecutable);
    }

    public async Task<Result<GitStatus>> GetStatusAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var prefix = await GetWorkspacePrefixAsync(workspacePath, cancellationToken);
        if (prefix.IsFailure)
        {
            return prefix.Error!;
        }

        var status = await RunAsync(
            workspacePath,
            ["--no-optional-locks", "status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all", "--", ".", ExcludeLocalFolder],
            cancellationToken);
        if (status.IsFailure)
        {
            return status.Error!;
        }

        if (!status.Value.Succeeded)
        {
            return GitErrors.CommandFailed("status", status.Value.StandardError);
        }

        var merging = await IsMergingAsync(workspacePath, cancellationToken);
        if (merging.IsFailure)
        {
            return merging.Error!;
        }

        return PorcelainStatusParser.Parse(status.Value.StandardOutput, prefix.Value, merging.Value);
    }

    public async Task<Result<string>> CommitAsync(
        string workspacePath,
        CommitMessage message,
        IReadOnlyList<string>? paths,
        CancellationToken cancellationToken = default)
    {
        var prefix = await GetWorkspacePrefixAsync(workspacePath, cancellationToken);
        if (prefix.IsFailure)
        {
            return prefix.Error!;
        }

        var merging = await IsMergingAsync(workspacePath, cancellationToken);
        if (merging.IsFailure)
        {
            return merging.Error!;
        }

        if (merging.Value && paths is not null)
        {
            return GitErrors.PartialCommitDuringMerge();
        }

        // Literal pathspecs, so file names are never read as glob patterns.
        List<string> pathspecs = paths is null
            ? [".", ExcludeLocalFolder]
            : [.. paths.Select(p => ":(literal)" + p), ExcludeLocalFolder];

        var add = await RunCheckedAsync(workspacePath, "add", ["add", "--all", "--", .. pathspecs], cancellationToken);
        if (add.IsFailure)
        {
            return add.Error!;
        }

        // A merge commit must include the whole index; otherwise commit only the selected paths,
        // leaving anything else the user staged untouched.
        List<string> commitArguments = merging.Value
            ? ["commit", "--message", message.Value]
            : ["commit", "--message", message.Value, "--only", "--", .. pathspecs];

        var commit = await RunCheckedAsync(workspacePath, "commit", commitArguments, cancellationToken);
        if (commit.IsFailure)
        {
            return commit.Error!;
        }

        var head = await RunCheckedAsync(workspacePath, "rev-parse", ["rev-parse", "HEAD"], cancellationToken);
        if (head.IsFailure)
        {
            return head.Error!;
        }

        return head.Value.StandardOutput.Trim();
    }

    public async Task<Result<PullOutcome>> PullAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var prefix = await GetWorkspacePrefixAsync(workspacePath, cancellationToken);
        if (prefix.IsFailure)
        {
            return prefix.Error!;
        }

        var headBefore = await GetHeadAsync(workspacePath, cancellationToken);
        if (headBefore.IsFailure)
        {
            return headBefore.Error!;
        }

        // Always merge: rebasing would rewrite the user's local commits.
        var pull = await RunAsync(workspacePath, ["pull", "--no-rebase", "--no-edit"], cancellationToken);
        if (pull.IsFailure)
        {
            return pull.Error!;
        }

        if (!pull.Value.Succeeded)
        {
            var conflicts = await RunAsync(
                workspacePath,
                ["diff", "--name-only", "--diff-filter=U", "-z"],
                cancellationToken);
            if (conflicts.IsFailure)
            {
                return conflicts.Error!;
            }

            if (conflicts.Value.Succeeded && conflicts.Value.StandardOutput.Length > 0)
            {
                return PullOutcome.Conflicted;
            }

            return GitErrors.CommandFailed("pull", Details(pull.Value));
        }

        var headAfter = await GetHeadAsync(workspacePath, cancellationToken);
        if (headAfter.IsFailure)
        {
            return headAfter.Error!;
        }

        return headAfter.Value == headBefore.Value ? PullOutcome.UpToDate : PullOutcome.Updated;
    }

    public async Task<Result<Unit>> PushAsync(
        string workspacePath,
        bool setUpstream,
        CancellationToken cancellationToken = default)
    {
        var prefix = await GetWorkspacePrefixAsync(workspacePath, cancellationToken);
        if (prefix.IsFailure)
        {
            return prefix.Error!;
        }

        List<string> arguments = ["push", "--porcelain"];
        if (setUpstream)
        {
            var remote = await GetDefaultRemoteAsync(workspacePath, cancellationToken);
            if (remote.IsFailure)
            {
                return remote.Error!;
            }

            arguments.AddRange(["--set-upstream", remote.Value, "HEAD"]);
        }

        var push = await RunAsync(workspacePath, arguments, cancellationToken);
        if (push.IsFailure)
        {
            return push.Error!;
        }

        // With --porcelain, each ref is reported on stdout; '!' marks a rejected ref.
        var rejected = push.Value.StandardOutput
            .Split('\n')
            .Where(line => line.StartsWith('!'))
            .ToList();
        if (rejected.Count > 0)
        {
            return GitErrors.PushRejected(string.Join('\n', rejected));
        }

        if (!push.Value.Succeeded)
        {
            return GitErrors.CommandFailed("push", Details(push.Value));
        }

        return Unit.Value;
    }

    /// <summary>
    /// Checks that the workspace is inside a work tree and returns its path relative to the
    /// repository root, with a trailing <c>/</c> (empty at the root).
    /// </summary>
    private async Task<Result<string>> GetWorkspacePrefixAsync(string workspacePath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspacePath))
        {
            return GitErrors.WorkspaceNotFound(workspacePath);
        }

        var result = await RunAsync(workspacePath, ["rev-parse", "--show-prefix"], cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        if (!result.Value.Succeeded)
        {
            var notARepository = result.Value.StandardError.Contains(
                "not a git repository",
                StringComparison.OrdinalIgnoreCase);
            return notARepository
                ? GitErrors.NotARepository(workspacePath)
                : GitErrors.CommandFailed("rev-parse", result.Value.StandardError);
        }

        return result.Value.StandardOutput.TrimEnd('\r', '\n');
    }

    private async Task<Result<bool>> IsMergingAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var result = await RunAsync(workspacePath, ["rev-parse", "--quiet", "--verify", "MERGE_HEAD"], cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        return result.Value.Succeeded;
    }

    /// <summary>The commit HEAD points to, or an empty string on a branch with no commits yet.</summary>
    private async Task<Result<string>> GetHeadAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var result = await RunAsync(workspacePath, ["rev-parse", "--quiet", "--verify", "HEAD"], cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        return result.Value.Succeeded ? result.Value.StandardOutput.Trim() : string.Empty;
    }

    /// <summary>The remote to publish a new branch to: <c>origin</c>, or the only remote.</summary>
    private async Task<Result<string>> GetDefaultRemoteAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var result = await RunCheckedAsync(workspacePath, "remote", ["remote"], cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var remotes = result.Value.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (remotes.Contains("origin", StringComparer.Ordinal))
        {
            return "origin";
        }

        if (remotes.Length == 1)
        {
            return remotes[0];
        }

        return GitErrors.NoRemote();
    }

    private async Task<Result<GitCommandResult>> RunCheckedAsync(
        string workspacePath,
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(workspacePath, arguments, cancellationToken);
        if (result.IsSuccess && !result.Value.Succeeded)
        {
            return GitErrors.CommandFailed(command, Details(result.Value));
        }

        return result;
    }

    private async Task<Result<GitCommandResult>> RunAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _runner.RunAsync(workspacePath, arguments, cancellationToken);
        }
        catch (GitNotFoundException)
        {
            return GitErrors.GitNotInstalled();
        }
    }

    /// <summary>What git printed about a failure; some commands report on stdout rather than stderr.</summary>
    private static string Details(GitCommandResult result) =>
        string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
}
