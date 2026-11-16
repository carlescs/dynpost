using Dynpost.Application.Common;
using Dynpost.Domain.Git;

namespace Dynpost.Application.Git;

/// <summary>
/// Credentials the user entered after git couldn't sign in to a remote, to try the command again.
/// </summary>
/// <param name="Url">The URL git couldn't sign in to (the target of the authentication error).</param>
/// <param name="Username">The user name.</param>
/// <param name="Password">The password or access token.</param>
public sealed record GitSignIn(string Url, string Username, string Password)
{
    /// <summary>Never shows the password, so logging the record can't leak it.</summary>
    public override string ToString() => $"{Username} @ {Url}";

    /// <summary>The credentials to hand to git: null without a sign-in, or why they can't be used.</summary>
    internal static Result<GitCredentials?> Check(GitSignIn? signIn)
    {
        if (signIn is null)
        {
            return Result<GitCredentials?>.Success(null);
        }

        return GitCredentials.TryCreate(signIn.Url, signIn.Username, signIn.Password, out var credentials, out var error)
            ? Result<GitCredentials?>.Success(credentials)
            : Result<GitCredentials?>.Failure(GitErrors.InvalidCredentials(error));
    }
}
