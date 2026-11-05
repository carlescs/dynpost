using System.Diagnostics.CodeAnalysis;

namespace Missive.Domain.Git;

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

    public override string ToString() => Value;
}
