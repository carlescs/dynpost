using System.Diagnostics.CodeAnalysis;

namespace Dynpost.Domain.Git;

/// <summary>
/// A username and password (or access token) for one HTTP(S) git host, entered by the user when git
/// couldn't sign in. Only ever handed to git for that host: <see cref="Scope"/> is the URL's scheme,
/// host and port, without any user name or path.
/// </summary>
public sealed record GitCredentials
{
    private GitCredentials(string protocol, string host, string username, string password)
    {
        Protocol = protocol;
        Host = host;
        Username = username;
        Password = password;
    }

    /// <summary><c>https</c> or <c>http</c>.</summary>
    public string Protocol { get; }

    /// <summary>The host name, with <c>:port</c> when the URL names a non-default port.</summary>
    public string Host { get; }

    public string Username { get; }

    public string Password { get; }

    /// <summary>The URL the credentials are for, as git's <c>credential.&lt;url&gt;.*</c> settings match it.</summary>
    public string Scope => $"{Protocol}://{Host}";

    /// <param name="url">The remote's URL, or any URL on the same host.</param>
    public static bool TryCreate(
        string? url,
        string? username,
        string? password,
        [NotNullWhen(true)] out GitCredentials? credentials,
        [NotNullWhen(false)] out string? error)
    {
        credentials = null;
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host))
        {
            error = "Signing in from Dynpost works for HTTP(S) remotes only.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            error = "Enter your user name.";
            return false;
        }

        if (string.IsNullOrEmpty(password))
        {
            error = "Enter your password or access token.";
            return false;
        }

        // Git's credential protocol is line based: a line break would end the value early.
        if (username.Any(char.IsControl) || password.Any(char.IsControl))
        {
            error = "The user name and password must be a single line.";
            return false;
        }

        var host = uri.IsDefaultPort ? uri.IdnHost : $"{uri.IdnHost}:{uri.Port}";
        credentials = new GitCredentials(uri.Scheme, host, username.Trim(), password);
        error = null;
        return true;
    }

    /// <summary>Never shows the password, so logging the record can't leak it.</summary>
    public override string ToString() => $"{Username} @ {Scope}";
}
