using Dynpost.Domain.Git;

namespace Dynpost.Infrastructure.Git;

/// <summary>
/// Hands credentials the user entered to one git command, for their host only. The command's
/// <c>-c</c> settings first clear the credential helpers configured for that host, so a stale
/// saved login isn't tried first, then add a helper that answers git's request from environment
/// variables. The secret is never on the command line, so other processes can't read it there,
/// nor is it written to any git configuration. The user's own helpers save it afterwards through
/// <c>git credential approve</c>, once the command proved it works.
/// </summary>
internal static class GitCredentialOverride
{
    public const string UsernameVariable = "DYNPOST_GIT_USERNAME";
    public const string PasswordVariable = "DYNPOST_GIT_PASSWORD";

    /// <summary>
    /// A shell helper (git runs <c>!</c> helpers through its shell, also on Windows): it reads and
    /// drops git's request, then answers <c>get</c> with the variables. <c>store</c> and
    /// <c>erase</c> do nothing, so a failed sign-in can't remove a login saved by the user's helpers.
    /// </summary>
    private const string Helper =
        "!f() { cat >/dev/null; if test \"$1\" = get; then printf 'username=%s\\npassword=%s\\n' "
        + "\"$" + UsernameVariable + "\" \"$" + PasswordVariable + "\"; fi; }; f";

    /// <summary>The git arguments with the settings that use <paramref name="credentials"/> first.</summary>
    public static IReadOnlyList<string> Arguments(IReadOnlyList<string> arguments, GitCredentials? credentials) =>
        credentials is null
            ? arguments
            : ["-c", $"credential.{credentials.Scope}.helper=", "-c", $"credential.{credentials.Scope}.helper={Helper}", .. arguments];

    /// <summary>The environment variables the helper reads.</summary>
    public static IReadOnlyDictionary<string, string>? Environment(GitCredentials? credentials) =>
        credentials is null
            ? null
            : new Dictionary<string, string>
            {
                [UsernameVariable] = credentials.Username,
                [PasswordVariable] = credentials.Password,
            };

    /// <summary>The input of <c>git credential approve</c>, in git's credential format.</summary>
    public static string ApproveInput(GitCredentials credentials) =>
        $"protocol={credentials.Protocol}\nhost={credentials.Host}\nusername={credentials.Username}\npassword={credentials.Password}\n\n";
}
