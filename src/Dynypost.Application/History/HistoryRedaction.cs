using System.Text;
using Dynypost.Application.Http;
using Dynypost.Application.Requests;
using Dynypost.Domain.Http;

namespace Dynypost.Application.History;

/// <summary>
/// Prepares a response to be written to the history: the request as sent is already masked, so
/// what is left to hide are cookie values and the environment's secret values in the response
/// headers and body. Bodies over the size limit are cut off (text) or left out (binary).
/// </summary>
public static class HistoryRedaction
{
    public static (ResponseDto Response, bool BodyOmitted) Prepare(ResponseDto response, IReadOnlyCollection<string> secrets, long maxBodyBytes)
    {
        var headers = response.Headers
            .Select(header => new KeyValueDto(header.Key, MaskHeader(header.Key, header.Value, secrets)))
            .ToList();
        var cookies = response.Cookies?
            .Select(cookie => cookie with { Value = SecretMasking.Mask })
            .ToList();

        var body = response.Body;
        var truncated = response.Truncated;
        var omitted = false;
        if (response.BodyEncoding == ResponseBodyDecoder.Text)
        {
            body = SecretMasking.MaskSecrets(body, secrets);
            if (Encoding.UTF8.GetByteCount(body) > maxBodyBytes)
            {
                body = Cut(body, maxBodyBytes);
                truncated = true;
            }
        }
        else if (body.Length / 4L * 3 > maxBodyBytes)
        {
            body = string.Empty;
            omitted = true;
        }

        var prepared = response with
        {
            Headers = headers,
            Cookies = cookies,
            Body = body,
            Truncated = truncated,
            HistoryId = null,
        };
        return (prepared, omitted);
    }

    private static string MaskHeader(string name, string value, IReadOnlyCollection<string> secrets)
    {
        if (!string.Equals(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
        {
            return SecretMasking.MaskSecrets(value, secrets);
        }

        // Keep the cookie's name and attributes, which are worth comparing; hide its value.
        var equals = value.IndexOf('=');
        if (equals < 0)
        {
            return SecretMasking.Mask;
        }

        var semicolon = value.IndexOf(';', equals);
        var attributes = semicolon < 0 ? string.Empty : value[semicolon..];
        return $"{value[..(equals + 1)]}{SecretMasking.Mask}{SecretMasking.MaskSecrets(attributes, secrets)}";
    }

    /// <summary>The start of <paramref name="text"/> that fits in <paramref name="maxBytes"/> UTF-8 bytes.</summary>
    private static string Cut(string text, long maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var length = (int)Math.Min(bytes.Length, maxBytes);

        // Don't split a multi-byte character: back up to the start of one.
        while (length > 0 && length < bytes.Length && (bytes[length] & 0xC0) == 0x80)
        {
            length--;
        }

        return Encoding.UTF8.GetString(bytes, 0, length);
    }
}
