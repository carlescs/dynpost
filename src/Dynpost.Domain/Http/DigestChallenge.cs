using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Dynpost.Domain.Http;

/// <summary>The user name and password a request answers a Digest challenge with.</summary>
public sealed record DigestCredentials(string Username, string Password);

/// <summary>
/// A <c>Digest</c> challenge from a <c>WWW-Authenticate</c> header (RFC 7616), and the
/// <c>Authorization</c> header that answers it. Supports the MD5 and SHA-256 algorithms (and their
/// <c>-sess</c> variants), <c>qop=auth</c>, <c>qop=auth-int</c> and the older form without qop.
/// </summary>
public sealed record DigestChallenge(
    string Realm,
    string Nonce,
    string? Opaque,
    string Algorithm,
    IReadOnlyList<string> Qop,
    bool UserHash = false)
{
    private static readonly string[] SupportedAlgorithms = ["SHA-256", "SHA-256-sess", "MD5", "MD5-sess"];

    /// <summary>
    /// The best supported Digest challenge in <paramref name="headers"/> (the values of the
    /// response's <c>WWW-Authenticate</c> headers): SHA-256 before MD5. <c>null</c> when there is
    /// none.
    /// </summary>
    public static DigestChallenge? Parse(IEnumerable<string> headers)
    {
        var challenges = new List<DigestChallenge>();
        foreach (var header in headers)
        {
            foreach (var (scheme, parameters) in Challenges(header))
            {
                if (!scheme.Equals("Digest", StringComparison.OrdinalIgnoreCase)
                    || !parameters.TryGetValue("nonce", out var nonce))
                {
                    continue;
                }

                var algorithm = parameters.GetValueOrDefault("algorithm") is { Length: > 0 } named ? named : "MD5";
                var supported = SupportedAlgorithms.FirstOrDefault(a => a.Equals(algorithm, StringComparison.OrdinalIgnoreCase));
                if (supported is null)
                {
                    continue;
                }

                var qop = (parameters.GetValueOrDefault("qop") ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(q => q.ToLowerInvariant())
                    .ToList();
                challenges.Add(new DigestChallenge(
                    parameters.GetValueOrDefault("realm") ?? string.Empty,
                    nonce,
                    parameters.GetValueOrDefault("opaque"),
                    supported,
                    qop,
                    string.Equals(parameters.GetValueOrDefault("userhash"), "true", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return challenges.OrderBy(c => Array.IndexOf(SupportedAlgorithms, c.Algorithm) / 2).FirstOrDefault();
    }

    /// <summary>Whether the challenge can be answered: it offers <c>auth</c> or <c>auth-int</c>, or no qop at all.</summary>
    public bool IsAnswerable => Qop.Count == 0 || Qop.Contains("auth") || Qop.Contains("auth-int");

    /// <summary>
    /// The <c>Authorization</c> header value answering the challenge.
    /// </summary>
    /// <param name="uri">The request target: the URL's path and query.</param>
    /// <param name="clientNonce">A fresh random value chosen by the client.</param>
    /// <param name="body">The request body, for <c>qop=auth-int</c> (only used when the server offers nothing else).</param>
    public string Authorization(
        DigestCredentials credentials,
        string method,
        string uri,
        string clientNonce,
        int nonceCount = 1,
        byte[]? body = null)
    {
        var qop = Qop.Contains("auth") ? "auth" : Qop.Contains("auth-int") ? "auth-int" : null;
        var nc = nonceCount.ToString("x8", CultureInfo.InvariantCulture);
        var sha256 = Algorithm.StartsWith("SHA-256", StringComparison.Ordinal);
        string H(string text) => Hash(Encoding.UTF8.GetBytes(text), sha256);

        var a1 = H($"{credentials.Username}:{Realm}:{credentials.Password}");
        if (Algorithm.EndsWith("-sess", StringComparison.Ordinal))
        {
            a1 = H($"{a1}:{Nonce}:{clientNonce}");
        }

        var a2 = qop == "auth-int"
            ? H($"{method}:{uri}:{Hash(body ?? [], sha256)}")
            : H($"{method}:{uri}");
        var response = qop is null
            ? H($"{a1}:{Nonce}:{a2}")
            : H($"{a1}:{Nonce}:{nc}:{clientNonce}:{qop}:{a2}");

        var username = UserHash ? H($"{credentials.Username}:{Realm}") : credentials.Username;
        var builder = new StringBuilder("Digest ");
        builder.Append(CultureInfo.InvariantCulture, $"username=\"{Quote(username)}\", realm=\"{Quote(Realm)}\", nonce=\"{Quote(Nonce)}\", uri=\"{Quote(uri)}\", ");
        builder.Append(CultureInfo.InvariantCulture, $"algorithm={Algorithm}, response=\"{response}\"");
        if (qop is not null)
        {
            builder.Append(CultureInfo.InvariantCulture, $", qop={qop}, nc={nc}, cnonce=\"{Quote(clientNonce)}\"");
        }

        if (Opaque is not null)
        {
            builder.Append(CultureInfo.InvariantCulture, $", opaque=\"{Quote(Opaque)}\"");
        }

        if (UserHash)
        {
            builder.Append(", userhash=true");
        }

        return builder.ToString();
    }

    private static string Hash(byte[] data, bool sha256) =>
        Convert.ToHexStringLower(sha256 ? SHA256.HashData(data) : MD5.HashData(data));

    private static string Quote(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// Splits a <c>WWW-Authenticate</c> value into its challenges: a scheme followed by
    /// comma-separated <c>name=value</c> or <c>name="quoted value"</c> parameters.
    /// </summary>
    private static IEnumerable<(string Scheme, Dictionary<string, string> Parameters)> Challenges(string header)
    {
        var position = 0;
        string? scheme = null;
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (position < header.Length)
        {
            SkipSeparators(header, ref position);
            var token = ReadToken(header, ref position);
            if (token.Length == 0)
            {
                position++;
                continue;
            }

            SkipSpaces(header, ref position);
            if (position < header.Length && header[position] == '=')
            {
                position++;
                SkipSpaces(header, ref position);
                var value = position < header.Length && header[position] == '"'
                    ? ReadQuoted(header, ref position)
                    : ReadToken(header, ref position);
                if (scheme is not null)
                {
                    parameters.TryAdd(token, value);
                }
            }
            else
            {
                // A token not followed by '=' starts the next challenge.
                if (scheme is not null)
                {
                    yield return (scheme, parameters);
                }

                scheme = token;
                parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        if (scheme is not null)
        {
            yield return (scheme, parameters);
        }
    }

    private static void SkipSpaces(string text, ref int position)
    {
        while (position < text.Length && text[position] is ' ' or '\t')
        {
            position++;
        }
    }

    private static void SkipSeparators(string text, ref int position)
    {
        while (position < text.Length && text[position] is ' ' or '\t' or ',')
        {
            position++;
        }
    }

    private static string ReadToken(string text, ref int position)
    {
        var start = position;
        while (position < text.Length && text[position] is not (' ' or '\t' or ',' or '=' or '"'))
        {
            position++;
        }

        return text[start..position];
    }

    private static string ReadQuoted(string text, ref int position)
    {
        var builder = new StringBuilder();
        position++; // opening quote
        while (position < text.Length && text[position] != '"')
        {
            if (text[position] == '\\' && position + 1 < text.Length)
            {
                position++;
            }

            builder.Append(text[position]);
            position++;
        }

        position++; // closing quote
        return builder.ToString();
    }
}
