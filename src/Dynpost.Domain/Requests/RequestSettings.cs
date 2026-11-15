namespace Dynpost.Domain.Requests;

/// <summary>The HTTP version a request asks for.</summary>
public enum HttpVersionPreference
{
    /// <summary>HTTP/1.1 only (the default).</summary>
    Http11,

    /// <summary>HTTP/2 when the server agrees to it (over TLS), else HTTP/1.1.</summary>
    Http2,
}

/// <summary>
/// How a request is sent: its timeout, redirects, TLS certificate checks and HTTP version. Each
/// value is optional: <c>null</c> inherits it from the folders above, and then the default applies
/// (60 seconds, follow up to 20 redirects, verify certificates, HTTP/1.1).
/// </summary>
/// <param name="Timeout">How long to wait for the whole response; <see cref="TimeSpan.Zero"/> waits for ever.</param>
/// <param name="FollowRedirects">Whether redirects are followed.</param>
/// <param name="MaxRedirects">How many redirects are followed at most.</param>
/// <param name="VerifyTls">Whether the server's TLS certificate must be valid and trusted.</param>
/// <param name="HttpVersion">The HTTP version to ask for.</param>
public sealed record RequestSettings(
    TimeSpan? Timeout = null,
    bool? FollowRedirects = null,
    int? MaxRedirects = null,
    bool? VerifyTls = null,
    HttpVersionPreference? HttpVersion = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public const int DefaultMaxRedirects = 20;

    /// <summary>The longest timeout that can be set (other than none).</summary>
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromHours(24);

    /// <summary>The most redirects that can be followed.</summary>
    public const int MaxRedirectsLimit = 100;

    /// <summary>Settings that set nothing: everything is inherited, or the default.</summary>
    public static RequestSettings Inherit { get; } = new();

    /// <summary>Whether nothing is set, so the file needs no <c>settings</c>.</summary>
    public bool IsEmpty => this == Inherit;

    /// <summary>These settings, with the values they don't set taken from <paramref name="parent"/>.</summary>
    public RequestSettings Over(RequestSettings parent) => new(
        Timeout ?? parent.Timeout,
        FollowRedirects ?? parent.FollowRedirects,
        MaxRedirects ?? parent.MaxRedirects,
        VerifyTls ?? parent.VerifyTls,
        HttpVersion ?? parent.HttpVersion);

    /// <summary>The timeout that applies; <see cref="TimeSpan.Zero"/> for none.</summary>
    public TimeSpan EffectiveTimeout => Timeout ?? DefaultTimeout;

    /// <summary>How many redirects are followed: none when they are turned off.</summary>
    public int EffectiveMaxRedirects => FollowRedirects == false ? 0 : MaxRedirects ?? DefaultMaxRedirects;

    public bool EffectiveVerifyTls => VerifyTls ?? true;

    public HttpVersionPreference EffectiveHttpVersion => HttpVersion ?? HttpVersionPreference.Http11;

    /// <summary>Checks the values are in range; empty when they are.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Timeout is { } timeout && (timeout < TimeSpan.Zero || timeout > MaxTimeout))
        {
            errors.Add("The timeout must be between 0 (no timeout) and 24 hours.");
        }

        if (MaxRedirects is { } max && (max < 0 || max > MaxRedirectsLimit))
        {
            errors.Add($"The most redirects to follow must be between 0 and {MaxRedirectsLimit}.");
        }

        return errors;
    }

    /// <summary>The name of an HTTP version in request files and DTOs: <c>1.1</c> or <c>2</c>.</summary>
    public static string VersionName(HttpVersionPreference version) => version switch
    {
        HttpVersionPreference.Http11 => "1.1",
        HttpVersionPreference.Http2 => "2",
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
    };

    public static bool TryParseVersion(string? name, out HttpVersionPreference version)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "1.1" or "http/1.1" or "http1.1":
                version = HttpVersionPreference.Http11;
                return true;
            case "2" or "2.0" or "http/2" or "http2":
                version = HttpVersionPreference.Http2;
                return true;
            default:
                version = HttpVersionPreference.Http11;
                return false;
        }
    }
}
