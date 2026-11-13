namespace Dynpost.Domain.Http;

/// <summary>Hides secrets in the request shown as sent, so it can be looked at or shared safely.</summary>
public static class SecretMasking
{
    /// <summary>What a hidden value is replaced with.</summary>
    public const string Mask = "••••••";

    /// <summary>Headers whose values are credentials whatever they contain.</summary>
    private static readonly HashSet<string> CredentialHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
    };

    /// <summary>
    /// Replaces every occurrence of a secret value in <paramref name="text"/>. Longer secrets are
    /// replaced first, so a secret that contains another is hidden whole.
    /// </summary>
    public static string MaskSecrets(string text, IReadOnlyCollection<string> secrets)
    {
        foreach (var secret in secrets.Where(s => s.Length > 0).OrderByDescending(s => s.Length))
        {
            text = text.Replace(secret, Mask, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>
    /// Masks a header value: credential headers keep only their scheme (<c>Bearer ••••••</c>),
    /// headers named in <paramref name="sensitiveNames"/> (such as an API key) are hidden whole,
    /// and secret values are hidden anywhere else.
    /// </summary>
    public static string MaskHeader(string name, string value, IReadOnlyCollection<string> sensitiveNames, IReadOnlyCollection<string> secrets)
    {
        if (CredentialHeaders.Contains(name))
        {
            var space = value.IndexOf(' ');
            return space > 0 ? $"{value[..space]} {Mask}" : Mask;
        }

        if (sensitiveNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return Mask;
        }

        return MaskSecrets(value, secrets);
    }

    /// <summary>
    /// Masks a URL: the values of query parameters named in <paramref name="sensitiveParams"/>, the
    /// password of user information, and secret values anywhere (also when percent-encoded).
    /// </summary>
    public static string MaskUrl(Uri url, IReadOnlyCollection<string> sensitiveParams, IReadOnlyCollection<string> secrets)
    {
        var text = url.AbsoluteUri;
        if (url.UserInfo.Contains(':'))
        {
            var user = url.UserInfo[..url.UserInfo.IndexOf(':')];
            text = text.Replace(url.UserInfo + "@", $"{user}:{Mask}@", StringComparison.Ordinal);
        }

        if (sensitiveParams.Count > 0 && url.Query.Length > 1)
        {
            var queryStart = text.IndexOf('?');
            var fragmentStart = text.IndexOf('#', queryStart);
            var query = fragmentStart < 0 ? text[(queryStart + 1)..] : text[(queryStart + 1)..fragmentStart];
            var masked = string.Join('&', query.Split('&').Select(pair =>
            {
                var equals = pair.IndexOf('=');
                var name = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
                return equals >= 0 && sensitiveParams.Contains(name, StringComparer.Ordinal) ? $"{pair[..equals]}={Mask}" : pair;
            }));
            text = text[..(queryStart + 1)] + masked + (fragmentStart < 0 ? string.Empty : text[fragmentStart..]);
        }

        var encoded = secrets.Where(s => s.Length > 0).Select(Uri.EscapeDataString).ToList();
        return MaskSecrets(text, [.. secrets, .. encoded]);
    }
}
