using System.Diagnostics.CodeAnalysis;

namespace Dynypost.Domain.Git;

/// <summary>
/// A non-empty commit message with LF line endings and no leading or trailing whitespace.
/// </summary>
public sealed record CommitMessage
{
    private CommitMessage(string value) => Value = value;

    public string Value { get; }

    /// <summary>The first line of the message.</summary>
    public string Summary => Value.Split('\n', 2)[0];

    public static bool TryCreate(string? text, [NotNullWhen(true)] out CommitMessage? message)
    {
        var normalized = (text ?? string.Empty).ReplaceLineEndings("\n").Trim();
        message = normalized.Length == 0 ? null : new CommitMessage(normalized);
        return message is not null;
    }

    public override string ToString() => Value;
}
