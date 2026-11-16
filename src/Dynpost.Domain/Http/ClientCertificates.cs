namespace Dynpost.Domain.Http;

/// <summary>
/// A client certificate set up for an environment: the TLS certificate (and private key) requests
/// present to servers that ask for one (mutual TLS). Kept on this computer only.
/// </summary>
/// <param name="Host">
/// The host it is used for: <c>api.example.com</c>, <c>*.example.com</c> (any sub-domain), either
/// with a <c>:port</c>, or empty for every host.
/// </param>
/// <param name="Certificate">The certificate file: PKCS#12 (<c>.pfx</c>/<c>.p12</c>) or PEM.</param>
/// <param name="Key">The PEM private key file, for a PEM certificate whose key is in its own file; empty otherwise.</param>
/// <param name="Passphrase">The password of the PKCS#12 file or encrypted key; empty for none.</param>
public sealed record ClientCertificateSetting(string Host, string Certificate, string Key = "", string Passphrase = "")
{
    /// <summary>Whether this certificate is for <paramref name="url"/>'s host (and port, when it names one).</summary>
    public bool Matches(Uri url)
    {
        var pattern = Host.Trim().ToLowerInvariant();
        if (pattern.Length == 0)
        {
            return true;
        }

        var host = url.IdnHost.ToLowerInvariant();
        var colon = pattern.LastIndexOf(':');
        if (colon > 0 && !pattern.EndsWith(']') && int.TryParse(pattern[(colon + 1)..], out var port))
        {
            if (port != url.Port)
            {
                return false;
            }

            pattern = pattern[..colon];
        }

        return pattern.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(pattern[1..], StringComparison.Ordinal) && host.Length > pattern.Length - 1
            : host == pattern;
    }

    /// <summary>The first of <paramref name="settings"/> that matches <paramref name="url"/>, or <c>null</c>.</summary>
    public static ClientCertificateSetting? For(IEnumerable<ClientCertificateSetting> settings, Uri url) =>
        settings.FirstOrDefault(setting => setting.Certificate.Length > 0 && setting.Matches(url));
}

/// <summary>A client certificate to present when sending a request, with its files' full paths.</summary>
/// <param name="KeyPath">The PEM private key file; <c>null</c> when the certificate file holds the key.</param>
/// <param name="Passphrase">The password of the PKCS#12 file or encrypted key; <c>null</c> for none.</param>
public sealed record ClientCertificate(string CertificatePath, string? KeyPath = null, string? Passphrase = null);
