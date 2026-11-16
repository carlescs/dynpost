namespace Dynpost.Domain.Git;

/// <summary>A stash entry: uncommitted changes git set aside, to bring back later.</summary>
/// <param name="Index">Its position in the stash list, newest first (<c>stash@{0}</c>).</param>
/// <param name="Id">The stash commit's id, which identifies the entry even when the list changes.</param>
/// <param name="Message">The message it was saved with, or git's own (<c>abc1234 Last commit subject</c>).</param>
/// <param name="Branch">The branch it was made on; <c>null</c> on a detached HEAD.</param>
/// <param name="CreatedAt">When it was made.</param>
public sealed record GitStash(int Index, string Id, string Message, string? Branch, DateTimeOffset? CreatedAt)
{
    /// <summary>How git names the entry now: <c>stash@{2}</c>.</summary>
    public string Ref => $"stash@{{{Index}}}";

    /// <summary>
    /// A stash message as git records it: on one line, without surrounding spaces; <c>null</c> when
    /// blank, so git writes its own.
    /// </summary>
    public static string? NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        return string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0));
    }
}
