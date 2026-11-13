using System.Text;

namespace Dynypost.Application.Http;

/// <summary>Decides whether a response body is text and decodes it.</summary>
internal static class ResponseBodyDecoder
{
    public const string Text = "text";
    public const string Base64 = "base64";

    /// <summary>Splits a Content-Type value into its media type and charset (either may be null).</summary>
    public static (string? MediaType, string? Charset) ParseContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return (null, null);
        }

        var parts = contentType.Split(';');
        var mediaType = parts[0].Trim().ToLowerInvariant();
        string? charset = null;
        foreach (var parameter in parts.Skip(1))
        {
            var index = parameter.IndexOf('=');
            if (index > 0 && parameter[..index].Trim().Equals("charset", StringComparison.OrdinalIgnoreCase))
            {
                charset = parameter[(index + 1)..].Trim().Trim('"');
            }
        }

        return (mediaType.Length == 0 ? null : mediaType, charset);
    }

    /// <summary>Returns the body as text, or as base64 with <see cref="Base64"/> when it is binary.</summary>
    public static (string Body, string Encoding) Decode(byte[] body, string? mediaType, string? charset)
    {
        if (body.Length == 0)
        {
            return (string.Empty, Text);
        }

        var encoding = GetEncoding(charset);
        var textual = mediaType is null ? LooksLikeText(body) : IsTextMediaType(mediaType);
        if (!textual)
        {
            return (Convert.ToBase64String(body), Base64);
        }

        // Honors a byte order mark if there is one; otherwise uses the charset (UTF-8 by default).
        using var reader = new StreamReader(new MemoryStream(body), encoding ?? new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return (reader.ReadToEnd(), Text);
    }

    private static bool IsTextMediaType(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.Ordinal)
        || mediaType.EndsWith("+json", StringComparison.Ordinal)
        || mediaType.EndsWith("+xml", StringComparison.Ordinal)
        || mediaType is "application/json" or "application/xml" or "application/javascript"
            or "application/ecmascript" or "application/x-www-form-urlencoded" or "application/graphql"
            or "application/x-ndjson" or "application/yaml" or "application/x-yaml";

    /// <summary>Without a content type, treats a body as text when it is valid UTF-8 without NUL characters.</summary>
    private static bool LooksLikeText(byte[] body)
    {
        if (Array.IndexOf(body, (byte)0) >= 0)
        {
            return false;
        }

        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(body);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static Encoding? GetEncoding(string? charset)
    {
        if (string.IsNullOrEmpty(charset))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
