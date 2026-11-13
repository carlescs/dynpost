using System.Diagnostics.CodeAnalysis;

namespace Dynpost.Domain.Git;

/// <summary>
/// A valid git remote name: letters, digits and <c>. _ - /</c>, as git requires for the refs
/// it creates under <c>refs/remotes/&lt;name&gt;/</c>.
/// </summary>
public sealed record RemoteName
{
    private RemoteName(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? text, [NotNullWhen(true)] out RemoteName? name, [NotNullWhen(false)] out string? error)
    {
        name = null;
        error = Validate(text?.Trim());
        if (error is null)
        {
            name = new RemoteName(text!.Trim());
        }

        return name is not null;
    }

    private static string? Validate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "The remote needs a name.";
        }

        if (!text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '/'))
        {
            return "Remote names can only contain letters, digits and . _ - /";
        }

        if (text.StartsWith('-') || text.StartsWith('.') || text.StartsWith('/'))
        {
            return "Remote names cannot start with -, . or /.";
        }

        if (text.EndsWith('/') || text.EndsWith('.') || text.EndsWith(".lock", StringComparison.Ordinal))
        {
            return "Remote names cannot end with /, . or .lock.";
        }

        if (text.Contains("..", StringComparison.Ordinal) || text.Contains("//", StringComparison.Ordinal) || text.Contains("/.", StringComparison.Ordinal))
        {
            return "Remote names cannot contain .., // or /.";
        }

        return null;
    }

    public override string ToString() => Value;
}
