using Dynpost.Application.Common;
using Dynpost.Application.Git;

namespace Dynpost.Infrastructure.Git;

/// <summary>
/// Turns what git printed about a failed command into an error the user can act on. Git runs with
/// <c>LC_ALL=C</c>, so its messages are untranslated and can be recognized here.
/// </summary>
internal static class GitFailure
{
    /// <summary>Signs that a command failed because another process holds a git lock file.</summary>
    public static bool IsLocked(string output) =>
        output.Contains(".lock': File exists", StringComparison.Ordinal);

    /// <param name="command">The git command that failed, for the fallback message.</param>
    /// <param name="details">What git printed about the failure.</param>
    public static Error Classify(string command, string details)
    {
        if (IsLocked(details))
        {
            return GitErrors.RepositoryLocked(FirstLine(details, ".lock'") ?? details);
        }

        if (Has(details, "detected dubious ownership"))
        {
            // Git prints the command that marks the folder safe; that is what the user needs.
            return GitErrors.UnsafeRepository(FirstLine(details, "safe.directory") ?? details);
        }

        if (Has(details, "Author identity unknown")
            || Has(details, "Please tell me who you are")
            || Has(details, "empty ident name")
            || Has(details, "unable to auto-detect email address")
            || Has(details, "no email was given and auto-detection is disabled")
            || Has(details, "no name was given and auto-detection is disabled"))
        {
            return GitErrors.IdentityUnknown();
        }

        if (Has(details, "would be overwritten by merge"))
        {
            return GitErrors.LocalChangesOverwritten(OverwrittenPaths(details));
        }

        if (Has(details, "refusing to merge unrelated histories"))
        {
            return GitErrors.UnrelatedHistories();
        }

        if (Has(details, "terminal prompts disabled")
            || Has(details, "could not read Username")
            || Has(details, "could not read Password")
            || Has(details, "Authentication failed")
            || Has(details, "Permission denied (publickey")
            || Has(details, "Host key verification failed")
            || Has(details, "The requested URL returned error: 401")
            || Has(details, "The requested URL returned error: 403")
            || Has(details, "Invalid username or password"))
        {
            return GitErrors.AuthenticationFailed(details);
        }

        if (Has(details, "Could not resolve host")
            || Has(details, "unable to access")
            || Has(details, "Connection refused")
            || Has(details, "Connection timed out")
            || Has(details, "Operation timed out")
            || Has(details, "Network is unreachable")
            || Has(details, "Could not read from remote repository")
            || Has(details, "does not appear to be a git repository")
            || Has(details, "Repository not found"))
        {
            return GitErrors.RemoteUnreachable(details);
        }

        return GitErrors.CommandFailed(command, details);
    }

    /// <summary>
    /// The files git lists, one per tab-indented line, after "...would be overwritten by merge:".
    /// </summary>
    internal static IReadOnlyList<string> OverwrittenPaths(string details)
    {
        var paths = new List<string>();
        var listing = false;
        foreach (var line in details.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.Contains("would be overwritten by", StringComparison.Ordinal))
            {
                listing = true;
                continue;
            }

            if (listing && line.StartsWith('\t'))
            {
                paths.Add(line.Trim());
            }
            else
            {
                listing = false;
            }
        }

        return paths.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool Has(string details, string text) => details.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static string? FirstLine(string details, string containing) =>
        details
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Contains(containing, StringComparison.Ordinal));
}
