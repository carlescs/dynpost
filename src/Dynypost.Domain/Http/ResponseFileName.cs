using System.Text;

namespace Dynypost.Domain.Http;

/// <summary>Suggests a file name for saving a response body.</summary>
public static class ResponseFileName
{
    /// <summary>The name used when neither the response nor the URL suggests one.</summary>
    public const string DefaultStem = "response";

    private const int MaxLength = 120;

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/json"] = ".json",
        ["application/xml"] = ".xml",
        ["text/xml"] = ".xml",
        ["text/html"] = ".html",
        ["text/plain"] = ".txt",
        ["text/csv"] = ".csv",
        ["text/css"] = ".css",
        ["text/javascript"] = ".js",
        ["application/javascript"] = ".js",
        ["text/markdown"] = ".md",
        ["application/yaml"] = ".yaml",
        ["application/x-yaml"] = ".yaml",
        ["text/yaml"] = ".yaml",
        ["application/x-ndjson"] = ".ndjson",
        ["application/pdf"] = ".pdf",
        ["application/zip"] = ".zip",
        ["application/gzip"] = ".gz",
        ["application/x-tar"] = ".tar",
        ["application/octet-stream"] = ".bin",
        ["application/wasm"] = ".wasm",
        ["application/msword"] = ".doc",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["application/vnd.ms-excel"] = ".xls",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["image/svg+xml"] = ".svg",
        ["image/x-icon"] = ".ico",
        ["image/vnd.microsoft.icon"] = ".ico",
        ["image/bmp"] = ".bmp",
        ["image/avif"] = ".avif",
        ["audio/mpeg"] = ".mp3",
        ["audio/wav"] = ".wav",
        ["audio/ogg"] = ".ogg",
        ["video/mp4"] = ".mp4",
        ["video/webm"] = ".webm",
        ["font/woff"] = ".woff",
        ["font/woff2"] = ".woff2",
    };

    /// <summary>
    /// The name from <c>Content-Disposition</c> when it has one; otherwise the last segment of the
    /// URL's path, or <see cref="DefaultStem"/>, with an extension for the media type when it has
    /// none. Characters that aren't allowed in file names are replaced.
    /// </summary>
    /// <param name="mediaType">The media type without parameters, e.g. <c>application/json</c>.</param>
    public static string Suggest(string? contentDisposition, Uri? url, string? mediaType)
    {
        if (FromContentDisposition(contentDisposition) is { } disposition)
        {
            return disposition;
        }

        var stem = url is null ? null : Clean(Uri.UnescapeDataString(url.AbsolutePath.TrimEnd('/').Split('/')[^1]));
        var extension = ExtensionFor(mediaType);
        if (string.IsNullOrEmpty(stem))
        {
            return DefaultStem + (extension ?? string.Empty);
        }

        return extension is not null && !HasExtension(stem) ? Truncate(stem + extension) : stem;
    }

    /// <summary>The usual extension (with its dot) for a media type, or <c>null</c> for an unknown one.</summary>
    public static string? ExtensionFor(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return null;
        }

        var type = mediaType.Split(';')[0].Trim();
        if (Extensions.TryGetValue(type, out var extension))
        {
            return extension;
        }

        if (type.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
        {
            return ".json";
        }

        return type.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) ? ".xml" : null;
    }

    /// <summary>The extension of a file name (with its dot), or <c>null</c> when it has none.</summary>
    public static string? ExtensionOf(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 && dot < fileName.Length - 1 ? fileName[dot..] : null;
    }

    private static bool HasExtension(string name) => ExtensionOf(name) is not null;

    /// <summary>The <c>filename*</c> (RFC 5987) or <c>filename</c> parameter, cleaned.</summary>
    private static string? FromContentDisposition(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        string? plain = null;
        string? extended = null;
        foreach (var parameter in SplitParameters(header).Skip(1))
        {
            var index = parameter.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var name = parameter[..index].Trim();
            var value = parameter[(index + 1)..].Trim();
            if (name.Equals("filename*", StringComparison.OrdinalIgnoreCase))
            {
                extended = DecodeExtended(value);
            }
            else if (name.Equals("filename", StringComparison.OrdinalIgnoreCase))
            {
                plain = Unquote(value);
            }
        }

        var cleaned = Clean(extended ?? plain);
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    /// <summary>Splits on semicolons that are not inside double quotes.</summary>
    private static IEnumerable<string> SplitParameters(string header)
    {
        var start = 0;
        var quoted = false;
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] == '"')
            {
                quoted = !quoted;
            }
            else if (header[i] == ';' && !quoted)
            {
                yield return header[start..i];
                start = i + 1;
            }
        }

        yield return header[start..];
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            return value;
        }

        var builder = new StringBuilder();
        for (var i = 1; i < value.Length - 1; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length - 1)
            {
                i++;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    /// <summary>Decodes <c>charset'language'percent-encoded</c>; only UTF-8 and Latin-1 are understood.</summary>
    private static string? DecodeExtended(string value)
    {
        var parts = value.Split('\'', 3);
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var encoding = parts[0].Equals("iso-8859-1", StringComparison.OrdinalIgnoreCase)
                ? Encoding.Latin1
                : Encoding.UTF8;
            var bytes = new List<byte>();
            var text = parts[2];
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '%' && i + 2 < text.Length)
                {
                    bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                    i += 2;
                }
                else
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(text[i].ToString()));
                }
            }

            return encoding.GetString(bytes.ToArray());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Keeps only the last path segment and replaces characters that Windows, macOS or Linux
    /// don't allow in file names. Names made only of dots become empty.
    /// </summary>
    private static string? Clean(string? name)
    {
        if (name is null)
        {
            return null;
        }

        name = name.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsControl(c) || "<>:\"|?*".Contains(c) ? '_' : c);
        }

        var cleaned = builder.ToString().Trim().TrimEnd('.');
        return cleaned.Trim('.').Length == 0 ? string.Empty : Truncate(cleaned);
    }

    private static string Truncate(string name)
    {
        if (name.Length <= MaxLength)
        {
            return name;
        }

        var extension = ExtensionOf(name) ?? string.Empty;
        return extension.Length >= MaxLength ? name[..MaxLength] : name[..(MaxLength - extension.Length)] + extension;
    }
}
