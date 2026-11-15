using System.Globalization;
using System.Text;
using Dynpost.Application.Common;
using Dynpost.Application.Interchange;
using Dynpost.Domain.Requests;

namespace Dynpost.Infrastructure.Interchange;

/// <summary>
/// Reads and writes cURL commands. Reading understands POSIX shell quoting (single and double
/// quotes, <c>$'…'</c>, backslashes), Windows cmd (<c>^</c> escapes and line breaks) and PowerShell
/// (backtick line breaks, <c>curl.exe</c>), and the options that describe a request; output-only
/// options are ignored, and any other option is reported.
/// </summary>
public sealed class CurlConverter : ICurlConverter
{
    /// <summary>Options that take a value and only change what curl prints or where it writes.</summary>
    private static readonly HashSet<string> IgnoredWithValue = new(StringComparer.Ordinal)
    {
        "-o", "--output", "-w", "--write-out", "--connect-timeout", "--retry", "--retry-delay", "--retry-max-time",
        "-c", "--cookie-jar", "-D", "--dump-header", "--trace", "--trace-ascii", "--stderr", "--limit-rate",
    };

    /// <summary>Options without a value that only change what curl prints, or that Dynpost does anyway.</summary>
    private static readonly HashSet<string> IgnoredFlags = new(StringComparer.Ordinal)
    {
        "-s", "--silent", "-S", "--show-error", "-v", "--verbose", "-i", "--include", "--compressed", "-#",
        "--progress-bar", "-f", "--fail", "--fail-with-body", "-N", "--no-buffer", "-O", "--remote-name",
        "--globoff", "-g", "--no-progress-meter", "-q", "--disable", "--http1.1", "--basic", "--location-trusted",
    };

    /// <summary>Short options that take a value (the rest of a short-option group is that value).</summary>
    private const string ShortWithValue = "XHdFuAbemowcDKrUyYzEtT";

    public Result<ParsedCurl> Parse(string command)
    {
        List<string> words;
        try
        {
            words = Tokenize(command);
        }
        catch (FormatException ex)
        {
            return new Error(ErrorCodes.InvalidRequest, $"The cURL command can't be read: {ex.Message}");
        }

        if (words.Count == 0 || !IsCurl(words[0]))
        {
            return new Error(ErrorCodes.InvalidRequest, "Paste a command that starts with 'curl'.");
        }

        var warnings = new List<string>();
        string? method = null;
        string? url = null;
        var headers = new List<KeyValueEntry>();
        var data = new List<(string Kind, string Value)>();
        var form = new List<FormField>();
        string? user = null;
        var digest = false;
        var get = false;
        var head = false;
        var json = false;
        var settings = RequestSettings.Inherit;

        for (var i = 1; i < words.Count; i++)
        {
            var word = words[i];
            string Value()
            {
                if (i + 1 >= words.Count)
                {
                    throw new FormatException($"'{word}' needs a value.");
                }

                return words[++i];
            }

            // Short options can be grouped (-sSL) and take their value attached (-XPOST, -HAccept:x).
            if (word.Length > 2 && word[0] == '-' && word[1] != '-')
            {
                var group = word[1..];
                var expanded = new List<string>();
                for (var j = 0; j < group.Length; j++)
                {
                    var option = "-" + group[j];
                    expanded.Add(option);
                    if (ShortWithValue.Contains(group[j], StringComparison.Ordinal))
                    {
                        if (j + 1 < group.Length)
                        {
                            expanded.Add(group[(j + 1)..]);
                        }

                        break;
                    }
                }

                words.RemoveAt(i);
                words.InsertRange(i, expanded);
                word = words[i];
            }

            string? inline = null;
            if (word.StartsWith("--", StringComparison.Ordinal) && word.IndexOf('=') is var eq and > 2)
            {
                inline = word[(eq + 1)..];
                word = word[..eq];
            }

            string Arg() => inline ?? Value();

            try
            {
                switch (word)
                {
                    case "-X" or "--request":
                        method = Arg().ToUpperInvariant();
                        break;
                    case "-H" or "--header":
                        var header = Arg();
                        var colon = header.IndexOf(':');
                        if (colon > 0)
                        {
                            headers.Add(new KeyValueEntry(header[..colon].Trim(), header[(colon + 1)..].Trim()));
                        }
                        else if (header.EndsWith(';'))
                        {
                            headers.Add(new KeyValueEntry(header.TrimEnd(';').Trim(), string.Empty));
                        }

                        break;
                    case "-d" or "--data" or "--data-ascii":
                        data.Add(("data", Arg()));
                        break;
                    case "--data-raw":
                        data.Add(("raw", Arg()));
                        break;
                    case "--data-binary":
                        data.Add(("binary", Arg()));
                        break;
                    case "--data-urlencode":
                        data.Add(("urlencode", Arg()));
                        break;
                    case "--json":
                        data.Add(("raw", Arg()));
                        json = true;
                        break;
                    case "-F" or "--form":
                        form.Add(FormFieldOf(Arg(), literal: false, warnings));
                        break;
                    case "--form-string":
                        form.Add(FormFieldOf(Arg(), literal: true, warnings));
                        break;
                    case "-u" or "--user":
                        user = Arg();
                        break;
                    case "--digest":
                        digest = true;
                        break;
                    case "-G" or "--get":
                        get = true;
                        break;
                    case "-I" or "--head":
                        head = true;
                        break;
                    case "-k" or "--insecure":
                        settings = settings with { VerifyTls = false };
                        break;
                    case "-L" or "--location":
                        // Dynpost follows redirects by default.
                        break;
                    case "--max-redirs":
                        settings = settings with { MaxRedirects = int.Parse(Arg(), CultureInfo.InvariantCulture) };
                        break;
                    case "-m" or "--max-time":
                        settings = settings with { Timeout = TimeSpan.FromSeconds(double.Parse(Arg(), CultureInfo.InvariantCulture)) };
                        break;
                    case "--http2" or "--http2-prior-knowledge":
                        settings = settings with { HttpVersion = HttpVersionPreference.Http2 };
                        break;
                    case "--url":
                        url = Arg();
                        break;
                    case "-A" or "--user-agent":
                        headers.Add(new KeyValueEntry("User-Agent", Arg()));
                        break;
                    case "-e" or "--referer":
                        headers.Add(new KeyValueEntry("Referer", Arg()));
                        break;
                    case "-b" or "--cookie":
                        var cookie = Arg();
                        if (cookie.Contains('='))
                        {
                            headers.Add(new KeyValueEntry("Cookie", cookie));
                        }
                        else
                        {
                            warnings.Add($"Skipped --cookie {cookie}: Dynpost can't read cookies from a file.");
                        }

                        break;
                    case "--oauth2-bearer":
                        headers.Add(new KeyValueEntry("Authorization", $"Bearer {Arg()}"));
                        break;
                    case var ignored when IgnoredWithValue.Contains(ignored):
                        Arg();
                        break;
                    case var flag when IgnoredFlags.Contains(flag):
                        break;
                    case var option when option.StartsWith('-') && option.Length > 1:
                        warnings.Add($"Skipped {option}: Dynpost doesn't support it.");
                        break;
                    default:
                        if (url is null)
                        {
                            url = word;
                        }
                        else
                        {
                            warnings.Add($"Skipped the extra URL {word}: a request has one URL.");
                        }

                        break;
                }
            }
            catch (FormatException)
            {
                return new Error(ErrorCodes.InvalidRequest, $"'{word}' needs a valid value.");
            }
            catch (OverflowException)
            {
                return new Error(ErrorCodes.InvalidRequest, $"'{word}' needs a valid value.");
            }
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return new Error(ErrorCodes.InvalidRequest, "The cURL command has no URL.");
        }

        var (baseUrl, query) = InterchangeText.SplitQuery(url.Trim());
        var body = RequestBody.None;

        if (get && data.Count > 0)
        {
            // -G sends the data as the query string.
            foreach (var (_, value) in data)
            {
                query.AddRange(UrlEncodedPairs(value).Select(f => new KeyValueEntry(f.Name, f.Value)));
            }

            data.Clear();
        }

        var contentType = headers.LastOrDefault(h => string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
        if (form.Count > 0)
        {
            body = RequestBody.WithFields(BodyType.Form, form);
            if (data.Count > 0)
            {
                warnings.Add("Skipped the -d data: the command also sends a multipart (-F) body.");
            }
        }
        else if (data.Count > 0)
        {
            body = DataBody(data, contentType, json, warnings);
        }

        if (json)
        {
            headers = AddUnlessSet(headers, "Content-Type", "application/json");
            headers = AddUnlessSet(headers, "Accept", "application/json");
        }

        var auth = RequestAuth.Inherit;
        if (user is not null)
        {
            var separator = user.IndexOf(':');
            var name = separator < 0 ? user : user[..separator];
            var password = separator < 0 ? string.Empty : user[(separator + 1)..];
            auth = digest ? new DigestAuth(name, password) : new BasicAuth(name, password);
        }
        else if (headers.FirstOrDefault(h => string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) is { } authorization)
        {
            // A plain Bearer or Basic header becomes the matching auth, so the editor shows it.
            var value = authorization.Value;
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                auth = new BearerAuth(value[7..].Trim());
                headers.Remove(authorization);
            }
            else if (value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) && DecodeBasic(value[6..].Trim()) is { } basic)
            {
                auth = new BasicAuth(basic.Name, basic.Password);
                headers.Remove(authorization);
            }
        }

        var methodName = method ?? (head ? "HEAD" : body.Type != BodyType.None ? "POST" : "GET");
        if (!RequestMethods.TryParse(methodName, out var requestMethod))
        {
            return new Error(ErrorCodes.InvalidRequest, $"Dynpost doesn't support the {methodName} method.");
        }

        var request = new HttpRequestDefinition(
            RequestName(requestMethod, baseUrl),
            requestMethod,
            baseUrl,
            query,
            InterchangeText.WithoutImpliedContentType(headers, body.Type),
            auth,
            body)
        {
            Settings = settings,
        };
        request = request with { PathParams = PathParameters.Sync(request.Url, []) };

        var errors = request.Validate();
        return errors.Count > 0
            ? new Error(ErrorCodes.InvalidRequest, string.Join(" ", errors))
            : new ParsedCurl(request, warnings);
    }

    public string Write(CurlCommand command, CurlShell shell)
    {
        var parts = new List<string> { shell == CurlShell.PowerShell ? "curl.exe" : "curl" };
        var bodyImpliesPost = command.Body is not null;
        if (!(command.Method == "GET" && !bodyImpliesPost) && !(command.Method == "POST" && bodyImpliesPost))
        {
            parts.Add($"-X {command.Method}");
        }

        parts.Add(Quote(command.Url, shell));
        foreach (var (name, value) in command.Headers)
        {
            parts.Add($"-H {Quote($"{name}: {value}", shell)}");
        }

        if (command.User is { } user)
        {
            if (command.Digest)
            {
                parts.Add("--digest");
            }

            parts.Add($"-u {Quote($"{user.Name}:{user.Password}", shell)}");
        }

        switch (command.Body)
        {
            case CurlTextBody text:
                parts.Add($"--data-raw {Quote(text.Text, shell)}");
                break;
            case CurlFileBody file:
                parts.Add($"--data-binary {Quote("@" + file.Path, shell)}");
                break;
            case CurlUrlEncodedBody fields:
                foreach (var field in fields.Fields)
                {
                    parts.Add(field.Encode
                        ? $"--data-urlencode {Quote($"{field.Name}={field.Value}", shell)}"
                        : $"--data-raw {Quote($"{field.Name}={field.Value}", shell)}");
                }

                break;
            case CurlFormBody fields:
                foreach (var field in fields.Fields)
                {
                    var spec = new StringBuilder(field.Name).Append('=');
                    if (field.IsFile)
                    {
                        spec.Append('@').Append(field.Value);
                        if (field.ContentType.Length > 0)
                        {
                            spec.Append(";type=").Append(field.ContentType);
                        }

                        if (field.FileName.Length > 0)
                        {
                            spec.Append(";filename=").Append(field.FileName);
                        }

                        parts.Add($"-F {Quote(spec.ToString(), shell)}");
                    }
                    else
                    {
                        // --form-string sends the value as written, even when it starts with @ or <.
                        spec.Append(field.Value);
                        if (field.ContentType.Length > 0)
                        {
                            spec.Append(";type=").Append(field.ContentType);
                            parts.Add($"-F {Quote(spec.ToString(), shell)}");
                        }
                        else
                        {
                            parts.Add($"--form-string {Quote(spec.ToString(), shell)}");
                        }
                    }
                }

                break;
            case null:
                break;
        }

        if (command.FollowRedirects)
        {
            parts.Add("-L");
        }

        if (command.Insecure)
        {
            parts.Add("-k");
        }

        if (command.Http2)
        {
            parts.Add("--http2");
        }

        if (command.MaxTime is { } maxTime && maxTime > TimeSpan.Zero)
        {
            parts.Add($"--max-time {maxTime.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        var continuation = shell switch
        {
            CurlShell.Cmd => " ^\n  ",
            CurlShell.PowerShell => " `\n  ",
            _ => " \\\n  ",
        };
        return string.Join(continuation, parts);
    }

    /// <summary>Quotes a word for <paramref name="shell"/> so it reaches curl exactly as given.</summary>
    internal static string Quote(string value, CurlShell shell)
    {
        switch (shell)
        {
            case CurlShell.Cmd:
                // Inside double quotes cmd leaves ^ and & alone; curl.exe reads \" as a quote. cmd
                // can't continue a quoted word on the next line, so line breaks become spaces (fine
                // for JSON), and a %name% that names an environment variable is still expanded by cmd.
                var escaped = value.Replace("\"", "\\\"").Replace("\r", string.Empty).Replace('\n', ' ');
                return $"\"{escaped}\"";
            case CurlShell.PowerShell:
                return $"'{value.Replace("'", "''")}'";
            default:
                return value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || "-_./:=@,+%".Contains(c))
                    ? value
                    : $"'{value.Replace("'", "'\\''")}'";
        }
    }

    private static bool IsCurl(string word)
    {
        var name = word.Replace('\\', '/').Split('/')[^1].ToLowerInvariant();
        return name is "curl" or "curl.exe";
    }

    private static RequestBody DataBody(List<(string Kind, string Value)> data, string? contentType, bool json, List<string> warnings)
    {
        if (data.Count == 1 && data[0].Kind is "data" or "binary" && data[0].Value.StartsWith('@'))
        {
            var path = data[0].Value[1..];
            if (path == "-")
            {
                warnings.Add("Skipped the body read from standard input (@-).");
                return RequestBody.None;
            }

            if (data[0].Kind == "data")
            {
                warnings.Add($"The body is sent from {path} as is; curl's -d @file would also drop its line breaks.");
            }

            return new RequestBody(BodyType.Binary, path);
        }

        var mediaType = InterchangeText.MediaType(contentType);
        var urlEncoded = !json && data.Any(d => d.Kind == "urlencode")
            || (!json && (mediaType.Length == 0 || mediaType == "application/x-www-form-urlencoded") && data.All(d => LooksUrlEncoded(d.Value)));
        if (urlEncoded)
        {
            var fields = new List<FormField>();
            foreach (var (kind, value) in data)
            {
                if (kind == "urlencode")
                {
                    var separator = value.IndexOf('=');
                    fields.Add(separator < 0
                        ? new FormField(string.Empty, value, Encode: true)
                        : new FormField(value[..separator], value[(separator + 1)..], Encode: true));
                }
                else
                {
                    fields.AddRange(UrlEncodedPairs(value));
                }
            }

            return RequestBody.WithFields(BodyType.UrlEncoded, fields);
        }

        // Several -d are joined with '&', as curl does.
        var text = string.Join('&', data.Select(d => d.Value));
        var type = json || InterchangeText.IsJson(mediaType) ? BodyType.Json : BodyType.Text;
        return new RequestBody(type, text);
    }

    private static bool LooksUrlEncoded(string value) =>
        value.Length > 0
        && value.Split('&').All(pair => pair.Length > 0 && pair.Split('=')[0].All(c => !char.IsWhiteSpace(c) && c is not ('{' or '[' or '"' or '<')));

    private static IEnumerable<FormField> UrlEncodedPairs(string value) =>
        value.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair =>
        {
            var separator = pair.IndexOf('=');
            return separator < 0 ? new FormField(pair, string.Empty) : new FormField(pair[..separator], pair[(separator + 1)..]);
        });

    private static FormField FormFieldOf(string spec, bool literal, List<string> warnings)
    {
        var separator = spec.IndexOf('=');
        var name = separator < 0 ? spec : spec[..separator];
        var rest = separator < 0 ? string.Empty : spec[(separator + 1)..];
        if (literal)
        {
            return new FormField(name, rest);
        }

        var options = rest.Split(';');
        var value = options[0];
        string Option(string key) =>
            options.Skip(1).Select(o => o.Trim()).FirstOrDefault(o => o.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?[(key.Length + 1)..].Trim('"') ?? string.Empty;

        if (value.StartsWith('@'))
        {
            return FormField.File(name, value[1..], Option("type"), Option("filename"));
        }

        if (value.StartsWith('<'))
        {
            warnings.Add($"The form field '{name}' reads its text from {value[1..]}; it is sent as that file instead.");
            return FormField.File(name, value[1..], Option("type"));
        }

        return new FormField(name, value, ContentType: Option("type"));
    }

    private static (string Name, string Password)? DecodeBasic(string encoded)
    {
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var separator = text.IndexOf(':');
            return separator < 0 ? null : (text[..separator], text[(separator + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static List<KeyValueEntry> AddUnlessSet(List<KeyValueEntry> headers, string name, string value) =>
        headers.Any(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase))
            ? headers
            : [.. headers, new KeyValueEntry(name, value)];

    private static string RequestName(RequestMethod method, string url)
    {
        var withoutScheme = url.Contains("://", StringComparison.Ordinal) ? url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..] : url;
        var slash = withoutScheme.IndexOf('/');
        var path = slash < 0 ? "/" : withoutScheme[slash..];
        var name = $"{method.ToName()} {path}";
        return name.Length > 80 ? name[..80] : name;
    }

    /// <summary>
    /// Splits a command into words like a shell would: POSIX quotes and escapes, or (when the
    /// command uses <c>^</c> line breaks or escapes) Windows cmd's, with line continuations of
    /// every kind removed.
    /// </summary>
    internal static List<string> Tokenize(string command)
    {
        var text = command.Replace("\r\n", "\n").Trim();
        var cmd = text.Contains("^\n", StringComparison.Ordinal) || text.Contains("^\"", StringComparison.Ordinal);
        text = text.Replace("`\n", " ");
        if (cmd)
        {
            text = text.Replace("^\n", " ");
        }

        var words = new List<string>();
        var current = new StringBuilder();
        var inWord = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (inWord)
                {
                    words.Add(current.ToString());
                    current.Clear();
                    inWord = false;
                }

                continue;
            }

            inWord = true;
            if (cmd)
            {
                if (c == '^' && i + 1 < text.Length)
                {
                    current.Append(text[++i]);
                }
                else if (c == '"')
                {
                    i++;
                    while (i < text.Length && text[i] != '"')
                    {
                        if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '"')
                        {
                            i++;
                        }
                        else if (text[i] == '^' && i + 1 < text.Length)
                        {
                            i++;
                        }

                        current.Append(text[i]);
                        i++;
                    }

                    if (i >= text.Length)
                    {
                        throw new FormatException("a double quote isn't closed.");
                    }
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '\\' when i + 1 < text.Length:
                    if (text[i + 1] != '\n')
                    {
                        current.Append(text[i + 1]);
                    }
                    else if (current.Length == 0)
                    {
                        inWord = false;
                    }

                    i++;
                    break;
                case '\'':
                    var end = text.IndexOf('\'', i + 1);
                    if (end < 0)
                    {
                        throw new FormatException("a single quote isn't closed.");
                    }

                    current.Append(text, i + 1, end - i - 1);
                    i = end;
                    break;
                case '$' when i + 1 < text.Length && text[i + 1] == '\'':
                    i += 2;
                    while (i < text.Length && text[i] != '\'')
                    {
                        if (text[i] == '\\' && i + 1 < text.Length)
                        {
                            i++;
                            current.Append(text[i] switch
                            {
                                'n' => '\n',
                                't' => '\t',
                                'r' => '\r',
                                '0' => '\0',
                                var other => other,
                            });
                        }
                        else
                        {
                            current.Append(text[i]);
                        }

                        i++;
                    }

                    if (i >= text.Length)
                    {
                        throw new FormatException("a $'…' quote isn't closed.");
                    }

                    break;
                case '"':
                    i++;
                    while (i < text.Length && text[i] != '"')
                    {
                        if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\' or '$' or '`' or '\n')
                        {
                            i++;
                            if (text[i] == '\n')
                            {
                                i++;
                                continue;
                            }
                        }

                        current.Append(text[i]);
                        i++;
                    }

                    if (i >= text.Length)
                    {
                        throw new FormatException("a double quote isn't closed.");
                    }

                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        if (inWord)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}
