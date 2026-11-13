using Dynypost.Application.Git;

namespace Dynypost.Infrastructure.Git;

/// <summary>
/// Watches a workspace with <see cref="FileSystemWatcher"/>s: one on the workspace folder and, when
/// the repository's git folder is elsewhere (the workspace is a subfolder of the repository, or a
/// linked worktree), one on that folder. Inside the git folder only the files that record what git
/// reports count (<c>HEAD</c>, <c>index</c>, refs, merge state); object writes and lock files don't.
/// </summary>
public sealed class FileSystemWorkingTreeWatcher(TimeSpan settleDelay) : IWorkingTreeWatcher
{
    /// <summary>How long changes must stop before the callback runs; editors and git write in bursts.</summary>
    public static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(400);

    public FileSystemWorkingTreeWatcher()
        : this(DefaultSettleDelay)
    {
    }

    public IDisposable Watch(string workspacePath, Action changed)
    {
        var workspace = Path.GetFullPath(workspacePath);
        var gitFolders = GitFolders.Find(workspace);
        var watch = new Subscription(workspace, gitFolders, settleDelay, changed);
        try
        {
            watch.Add(workspace);
            // A git folder outside the workspace needs its own watcher; one inside another watched
            // folder (a worktree's under the common one) is already covered.
            List<string> watched = [workspace];
            foreach (var folder in gitFolders.OrderBy(f => f.Length))
            {
                if (Directory.Exists(folder) && !watched.Any(w => IsUnder(folder, w)))
                {
                    watch.Add(folder);
                    watched.Add(folder);
                }
            }
        }
        catch
        {
            watch.Dispose();
            throw;
        }

        return watch;
    }

    private static bool IsUnder(string path, string folder) =>
        path.Length >= folder.Length
        && path.StartsWith(folder, PathComparison)
        && (path.Length == folder.Length || IsSeparator(path[folder.Length]) || IsSeparator(folder[^1]));

    private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed class Subscription : IDisposable
    {
        private readonly string _localFolder;
        private readonly IReadOnlyList<string> _gitFolders;
        private readonly Action _changed;
        private readonly TimeSpan _settleDelay;
        private readonly List<FileSystemWatcher> _watchers = [];
        private readonly Timer _timer;
        private volatile bool _disposed;

        public Subscription(string workspace, IReadOnlyList<string> gitFolders, TimeSpan settleDelay, Action changed)
        {
            _localFolder = Path.Combine(workspace, ".dynypost");
            _gitFolders = gitFolders;
            _settleDelay = settleDelay;
            _changed = changed;
            _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Add(string folder)
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Changed += OnEvent;
            watcher.Created += OnEvent;
            watcher.Deleted += OnEvent;
            watcher.Renamed += OnRenamed;
            // A lost batch of events (buffer overflow) may have held a change: re-read to be sure.
            watcher.Error += (_, _) => Schedule();
            _watchers.Add(watcher);
            watcher.EnableRaisingEvents = true;
        }

        private void OnEvent(object sender, FileSystemEventArgs e)
        {
            if (Counts(e.FullPath))
            {
                Schedule();
            }
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            if (Counts(e.FullPath) || Counts(e.OldFullPath))
            {
                Schedule();
            }
        }

        /// <summary>Whether a change to <paramref name="path"/> can change the git status.</summary>
        private bool Counts(string path)
        {
            if (IsUnder(path, _localFolder))
            {
                return false;
            }

            foreach (var gitFolder in _gitFolders)
            {
                if (IsUnder(path, gitFolder))
                {
                    return CountsInGitFolder(Path.GetRelativePath(gitFolder, path));
                }
            }

            return true;
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }

            _timer.Dispose();
        }

        private void Schedule()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _timer.Change(_settleDelay, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Disposed in the meantime.
            }
        }

        private void Fire()
        {
            if (!_disposed)
            {
                _changed();
            }
        }
    }

    /// <summary>
    /// Whether a change to the file at <paramref name="relativePath"/> inside a git folder can change
    /// the status: the checked-out branch and commit, the index, branches and remote-tracking
    /// branches, and the state of a merge. Lock files are written before the real file, which is
    /// what counts.
    /// </summary>
    internal static bool CountsInGitFolder(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        if (path.EndsWith(".lock", StringComparison.Ordinal))
        {
            return false;
        }

        return path is "HEAD" or "index" or "packed-refs" or "MERGE_HEAD" or "MERGE_MSG" or "info/exclude" or "refs"
            || path.StartsWith("refs/", StringComparison.Ordinal);
    }

    /// <summary>Locates the git folders of the repository holding a workspace.</summary>
    internal static class GitFolders
    {
        /// <summary>
        /// The repository's git folder and, for a linked worktree, its common git folder (where
        /// branches live); none when the workspace is not inside a repository yet (a <c>.git</c>
        /// folder created in the workspace later is still seen by the workspace's own watcher).
        /// </summary>
        public static IReadOnlyList<string> Find(string workspace)
        {
            for (var folder = new DirectoryInfo(workspace); folder is not null; folder = folder.Parent)
            {
                var dotGit = Path.Combine(folder.FullName, ".git");
                if (Directory.Exists(dotGit))
                {
                    return [dotGit];
                }

                if (File.Exists(dotGit))
                {
                    return FromGitFile(folder.FullName, dotGit);
                }
            }

            // Not a repository yet: a `git init` in the workspace creates `.git` there.
            return [Path.Combine(workspace, ".git")];
        }

        /// <summary>A worktree's or submodule's <c>.git</c> file says <c>gitdir: path</c>.</summary>
        private static IReadOnlyList<string> FromGitFile(string folder, string dotGit)
        {
            string? line;
            try
            {
                line = File.ReadLines(dotGit).FirstOrDefault();
            }
            catch (IOException)
            {
                return [dotGit];
            }

            const string prefix = "gitdir:";
            if (line is null || !line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return [dotGit];
            }

            var gitDir = Path.GetFullPath(Path.Combine(folder, line[prefix.Length..].Trim()));
            if (!Directory.Exists(gitDir))
            {
                return [dotGit];
            }

            var commonDirFile = Path.Combine(gitDir, "commondir");
            if (File.Exists(commonDirFile))
            {
                try
                {
                    var commonDir = Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(commonDirFile).Trim()));
                    if (Directory.Exists(commonDir))
                    {
                        return [gitDir, commonDir];
                    }
                }
                catch (IOException)
                {
                    // Fall back to the worktree's own git folder.
                }
            }

            return [gitDir];
        }
    }
}
