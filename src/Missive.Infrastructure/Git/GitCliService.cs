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

        var parsed = PorcelainStatusParser.Parse(status.Value.StandardOutput, prefix.Value, merging.Value);
        if (!merging.Value)
        {
            return parsed;
        }

        var mergeMessage = await ReadMergeMessageAsync(workspacePath, cancellationToken);
        if (mergeMessage.IsFailure)
        {
            return mergeMessage.Error!;
        }

        return parsed with { MergeMessage = mergeMessage.Value };
    }

    public async Task<Result<Unit>> InitAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var prefix = await GetWorkspacePrefixAsync(workspacePath, cancellationToken);
        if (prefix.IsSuccess)
        {
            return GitErrors.AlreadyARepository(workspacePath);
        }

        // Only a folder git positively reports as outside any repository is initialized; any other
        // failure (git missing, unsafe ownership...) is passed on rather than nesting a repository.
        if (prefix.Error!.Code != GitErrors.NotARepositoryCode)
        {
            return prefix.Error!;
        }

        var init = await RunCheckedAsync(workspacePath, "init", ["init", "--quiet"], cancellationToken);
        if (init.IsFailure)
        {
            return init.Error!;
        }

        try
        {
            await GitIgnoreFile.EnsureIgnoredAsync(workspacePath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return GitErrors.CommandFailed("init", $"The repository was created, but .gitignore could not be updated: {ex.Message}");
        }

        return Unit.Value;
    }

    public async Task<Result<GitConflict>> GetConflictAsync(
        string workspacePath,
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeRelativePath(path))
        {
            return GitErrors.NotConflicted(path);
        }

        var prefix = await GetWorkspacePrefixAsync(workspacePath, cancellationToken);
        if (prefix.IsFailure)
        {
            return prefix.Error!;
        }

        var versions = new string?[3];
        for (var stage = 1; stage <= 3; stage++)
        {
            var version = await ReadStageAsync(workspacePath, path, stage, cancellationToken);
            if (version.IsFailure)
            {
                return version.Error!;
            }

            versions[stage - 1] = version.Value;
        }

        var working = await ReadWorkingFileAsync(workspacePath, path, "show", cancellationToken);
        if (working.IsFailure)
        {
            return working.Error!;
        }

        return new GitConflict(path, versions[0], versions[1], versions[2], working.Value);
    }

    public async Task<Result<Unit>> ResolveConflictAsync(
        string workspacePath,
        string path,
        ConflictResolution resolution,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeRelativePath(path))
        {
            return GitErrors.NotConflicted(path);
        }

        var prefix = await GetWorkspacePrefixAsync(workspacePath, cancellationToken);
        if (prefix.IsFailure)
        {
            return prefix.Error!;
        }

        var pathspec = ":(literal)" + path;
        if (resolution == ConflictResolution.Edited)
        {
            // Stages the file as it is now, or its deletion if the user deleted it.
            var added = await RunCheckedAsync(workspacePath, "add", ["add", "--all", "--", pathspec], cancellationToken);
            return Done(added);
        }

        var stage = resolution == ConflictResolution.Ours ? 2 : 3;
        var version = await ReadStageAsync(workspacePath, path, stage, cancellationToken);
        if (version.IsFailure)
        {
            return version.Error!;
        }

        if (version.Value is null)
        {
            // The chosen side deleted the file: resolving to it means deleting it.
            var removed = await RunCheckedAsync(workspacePath, "rm", ["rm", "--quiet", "--force", "--", pathspec], cancellationToken);
            return Done(removed);
        }

        var side = resolution == ConflictResolution.Ours ? "--ours" : "--theirs";
        var checkout = await RunCheckedAsync(workspacePath, "checkout", ["checkout", side, "--", pathspec], cancellationToken);
        if (checkout.IsFailure)
        {
            return checkout.Error!;
        }

        var add = await RunCheckedAsync(workspacePath, "add", ["add", "--", pathspec], cancellationToken);
        return Done(add);
    }

    public async Task<Result<Unit>> AbortMergeAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var prefix = await GetWorkspacePrefixAsync(workspacePath, cancellationToken);
        if (prefix.IsFailure)
        {
            return prefix.Error!;
        }

        var abort = await RunCheckedAsync(workspacePath, "merge --abort", ["merge", "--abort"], cancellationToken);
        return Done(abort);
    }

    public async Task<Result<GitFileDiff>> GetFileDiffAsync(
        string workspacePath,
        GitChange change,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeRelativePath(change.Path) || (change.OriginalPath is { } from && !IsSafeRelativePath(from)))
        {
            return GitErrors.FileNotChanged(change.Path);
        }

        var prefix = await GetWorkspacePrefixAsync(workspacePath, cancellationToken);
        if (prefix.IsFailure)
        {
            return prefix.Error!;
        }

        // Both sides are read whatever the status says: a file removed from the index but kept on
        // disk is reported as deleted and untracked, yet exists in HEAD and in the working tree.
        // "./" makes the path relative to the working directory (the workspace) instead of the repository root.
        var committed = await RunAsync(workspacePath, ["show", $"HEAD:./{change.OriginalPath ?? change.Path}"], cancellationToken);
        if (committed.IsFailure)
        {
            return committed.Error!;
        }

        // Fails when there is no commit yet, or the file isn't in it.
        var original = committed.Value.Succeeded ? committed.Value.StandardOutput : null;

        var modified = await ReadWorkingFileAsync(workspacePath, change.Path, "diff", cancellationToken);
        if (modified.IsFailure)
        {
            return modified.Error!;
        }

        var binary = (original is not null && GitFileDiff.LooksBinary(original))
            || (modified.Value is not null && GitFileDiff.LooksBinary(modified.Value));

        return binary
            ? new GitFileDiff(change.Path, change.Status, change.OriginalPath, null, null, IsBinary: true)
            : new GitFileDiff(change.Path, change.Status, change.OriginalPath, original, modified.Value);
    }

    /// <summary>
    /// The working tree version of a file, for a diff or a conflict, or <c>null</c> when it doesn't
    /// exist. Links are never followed, so a link can't expose a file outside the workspace: a
    /// symbolic link reads as its target path, which is also what git stores for it, and a path
    /// through a linked folder is refused. <paramref name="command"/> names the operation in errors.
    /// </summary>
    private static async Task<Result<string?>> ReadWorkingFileAsync(
        string workspacePath,
        string path,
        string command,
        CancellationToken cancellationToken)
    {
        try
        {
            var current = workspacePath;
            var segments = path.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);
                var info = new FileInfo(current);
                if (!info.Exists && !Directory.Exists(current) && info.LinkTarget is null)
                {
                    return Result<string?>.Success(null);
                }

                if (info.LinkTarget is { } target)
                {
                    return i == segments.Length - 1
                        ? Result<string?>.Success(target)
                        : GitErrors.CommandFailed(command, $"'{path}' is inside a linked folder, so it is not read.");
                }
            }

            return File.Exists(current)
                ? Result<string?>.Success(await File.ReadAllTextAsync(current, cancellationToken))
                : Result<string?>.Success(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return GitErrors.CommandFailed(command, $"Could not read '{path}': {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<GitRemote>>> ListRemotesAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var prefix = await GetWorkspacePrefixAsync(workspacePath, cancellationToken);
        if (prefix.IsFailure)
        {
            return prefix.Error!;
        }

        var result = await RunCheckedAsync(workspacePath, "remote", ["remote", "--verbose"], cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        return Result<IReadOnlyList<GitRemote>>.Success(ParseRemotes(result.Value.StandardOutput));
    }

    public Task<Result<Unit>> AddRemoteAsync(
        string workspacePath,
        RemoteName name,
        RemoteUrl url,
        CancellationToken cancellationToken = default) =>
        RunRemoteAsync(workspacePath, "remote add", ["remote", "add", "--", name.Value, url.Value], cancellationToken);

    public Task<Result<Unit>> RenameRemoteAsync(
        string workspacePath,
        string name,
        RemoteName newName,
        CancellationToken cancellationToken = default) =>
        RunRemoteAsync(workspacePath, "remote rename", ["remote", "rename", "--", name, newName.Value], cancellationToken);

    public Task<Result<Unit>> SetRemoteUrlAsync(
        string workspacePath,
        string name,
        RemoteUrl url,
        CancellationToken cancellationToken = default) =>
        RunRemoteAsync(workspacePath, "remote set-url", ["remote", "set-url", "--", name, url.Value], cancellationToken);

    public Task<Result<Unit>> RemoveRemoteAsync(
        string workspacePath,
        string name,
        CancellationToken cancellationToken = default) =>
        RunRemoteAsync(workspacePath, "remote remove", ["remote", "remove", "--", name], cancellationToken);

    private async Task<Result<Unit>> RunRemoteAsync(
        string workspacePath,
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var prefix = await GetWorkspacePrefixAsync(workspacePath, cancellationToken);
        if (prefix.IsFailure)
        {
            return prefix.Error!;
        }

        // "--" keeps names and URLs from ever being read as options.
        return Done(await RunCheckedAsync(workspacePath, command, arguments, cancellationToken));
    }

    /// <summary>
    /// Parses <c>git remote --verbose</c>: a <c>name&lt;TAB&gt;url (fetch)</c> and a
    /// <c>name&lt;TAB&gt;url (push)</c> line per remote.
    /// </summary>
    internal static IReadOnlyList<GitRemote> ParseRemotes(string output)
    {
        var fetch = new Dictionary<string, string>(StringComparer.Ordinal);
        var push = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            var name = line[..tab];
            var rest = line[(tab + 1)..];
            if (rest.EndsWith(" (fetch)", StringComparison.Ordinal))
            {
                fetch[name] = rest[..^" (fetch)".Length];
            }
            else if (rest.EndsWith(" (push)", StringComparison.Ordinal))
            {
                push[name] = rest[..^" (push)".Length];
            }
        }

        return fetch.Keys
            .Union(push.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                var fetchUrl = fetch.GetValueOrDefault(name) ?? push.GetValueOrDefault(name) ?? string.Empty;
                return new GitRemote(name, fetchUrl, push.GetValueOrDefault(name) ?? fetchUrl);
            })
            .ToList();
    }

    /// <summary>
    /// One version of a conflicted file from the index: stage 1 is the common ancestor, 2 the
    /// current branch, 3 the incoming branch. <c>null</c> when that side has no such file.
    /// </summary>
    private async Task<Result<string?>> ReadStageAsync(string workspacePath, string path, int stage, CancellationToken cancellationToken)
    {
        // "./" makes the path relative to the working directory (the workspace) instead of the repository root.
        var result = await RunAsync(workspacePath, ["show", $":{stage}:./{path}"], cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        return Result<string?>.Success(result.Value.Succeeded ? result.Value.StandardOutput : null);
    }

    /// <summary>git's proposed merge commit message, without its comment lines; <c>null</c> if there is none.</summary>
    private async Task<Result<string?>> ReadMergeMessageAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var gitPath = await RunCheckedAsync(workspacePath, "rev-parse", ["rev-parse", "--git-path", "MERGE_MSG"], cancellationToken);
        if (gitPath.IsFailure)
        {
            return gitPath.Error!;
        }

        var file = Path.Combine(workspacePath, gitPath.Value.StandardOutput.TrimEnd('\r', '\n'));
        try
        {
            if (!File.Exists(file))
            {
                return Result<string?>.Success(null);
            }

            var lines = (await File.ReadAllLinesAsync(file, cancellationToken)).Where(line => !line.StartsWith('#'));
            var message = string.Join('\n', lines).Trim();
            return Result<string?>.Success(message.Length == 0 ? null : message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The message is only a suggestion; the merge can be committed without it.
            return Result<string?>.Success(null);
        }
    }

    private static Result<Unit> Done(Result<GitCommandResult> result) =>
        result.IsFailure ? Result<Unit>.Failure(result.Error!) : Result<Unit>.Success(Unit.Value);

    /// <summary>Defense in depth: a workspace-relative path that can't leave the workspace.</summary>
    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path)
        && !path.Replace('\\', '/').Split('/').Any(segment => segment is "" or "." or "..");

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
