using System.Diagnostics.CodeAnalysis;

namespace Dynpost.Domain.Git;

/// <summary>
/// The URL of a remote repository, as git accepts it: <c>https://…</c>, <c>ssh://…</c>,
/// <c>git@host:path</c> or a local path. Only checked for being one line that can't be taken
/// for a command-line option.
/// </summary>
public sealed record RemoteUrl
{
    private RemoteUrl(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? text, [NotNullWhen(true)] out RemoteUrl? url, [NotNullWhen(false)] out string? error)
    {
        url = null;
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            error = "Enter the remote's URL.";
        }
        else if (value.Any(char.IsControl))
        {
            error = "The URL must be a single line.";
        }
        else if (value.StartsWith('-'))
        {
            error = "The URL cannot start with -.";
        }
        else
        {
            error = null;
            url = new RemoteUrl(value);
        }

        return url is not null;
    }

    /// <summary>
    /// The folder name <c>git clone</c> would pick for the repository: the last path segment
    /// without a trailing <c>.git</c> (<c>https://host/team/api.git</c> and <c>git@host:team/api</c>
    /// give <c>api</c>), or null when the URL has no usable segment.
    /// </summary>
    public string? RepositoryName
    {
        get
        {
            var path = Value.Replace('\\', '/').TrimEnd('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^".git".Length].TrimEnd('/');
            }

            var start = path.LastIndexOfAny(['/', ':']) + 1;
            var name = path[start..];
            return name is "" or "." or ".." ? null : name;
        }
    }

    public override string ToString() => Value;
}
