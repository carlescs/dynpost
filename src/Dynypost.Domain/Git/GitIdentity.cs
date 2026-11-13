using System.Diagnostics.CodeAnalysis;

namespace Dynypost.Domain.Git;

/// <summary>
/// The name and email git records on commits (<c>user.name</c>, <c>user.email</c>). Both are
/// trimmed, non-empty, single-line and free of the <c>&lt;</c> and <c>&gt;</c> git uses to delimit
/// the email.
/// </summary>
public sealed record GitIdentity
{
    private GitIdentity(string name, string email)
    {
        Name = name;
        Email = email;
    }

    public string Name { get; }

    public string Email { get; }

    public static bool TryCreate(string? name, string? email, [NotNullWhen(true)] out GitIdentity? identity)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        var trimmedEmail = email?.Trim() ?? string.Empty;
        identity = IsValid(trimmedName) && IsValid(trimmedEmail) && trimmedEmail.Contains('@')
            ? new GitIdentity(trimmedName, trimmedEmail)
            : null;
        return identity is not null;
    }

    private static bool IsValid(string value) =>
        value.Length > 0 && !value.Any(c => c is '<' or '>' || char.IsControl(c));
}
