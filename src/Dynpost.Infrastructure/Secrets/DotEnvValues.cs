using System.Text;
using Dynpost.Application.Secrets;
using Dynpost.Application.Settings;
using Dynpost.Domain.Workspaces;
using static Dynpost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynpost.Infrastructure.Secrets;

/// <summary>
/// Reads the workspace's <c>.env</c> file when the user turned that on: <c>NAME=value</c> lines,
/// optionally after <c>export</c>, with <c>#</c> comments, <c>'single'</c> (as written) and
/// <c>"double"</c> quotes (with <c>\n</c>, <c>\t</c>, <c>\"</c> and <c>\\</c> escapes). A later line
/// wins over an earlier one with the same name.
/// </summary>
public sealed class DotEnvValues(IAppSettingsStore settings) : IDotEnvValues
{
    public const string FileName = ".env";

    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(string root, CancellationToken cancellationToken = default)
    {
        if ((await settings.LoadAsync(cancellationToken)).Secrets is not { UseDotEnv: true })
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var text = await ReadIfExistsAsync(FullPath(root, WorkspacePath.Root.Combine(FileName)), cancellationToken);
            return text is null ? new Dictionary<string, string>() : Parse(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>();
        }
    }

    internal static Dictionary<string, string> Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var name = line[..equals].Trim();
            var raw = line[(equals + 1)..].TrimStart();
            string value;
            if (raw.StartsWith('"'))
            {
                // A double-quoted value may go on over several lines.
                var builder = new StringBuilder();
                var rest = raw[1..];
                while (true)
                {
                    var closed = AppendDoubleQuoted(rest, builder);
                    if (closed || i + 1 >= lines.Length)
                    {
                        break;
                    }

                    builder.Append('\n');
                    rest = lines[++i];
                }

                value = builder.ToString();
            }
            else if (raw.StartsWith('\''))
            {
                var end = raw.IndexOf('\'', 1);
                value = end < 0 ? raw[1..] : raw[1..end];
            }
            else
            {
                var comment = raw.IndexOf(" #", StringComparison.Ordinal);
                value = (comment < 0 ? raw : raw[..comment]).TrimEnd();
            }

            if (name.Length > 0 && !name.Any(char.IsWhiteSpace))
            {
                values[name] = value;
            }
        }

        return values;
    }

    /// <summary>Appends a double-quoted value's text up to its closing quote; returns whether it was found.</summary>
    private static bool AppendDoubleQuoted(string text, StringBuilder builder)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                return true;
            }

            if (c == '\\' && i + 1 < text.Length)
            {
                i++;
                builder.Append(text[i] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    var other => other,
                });
            }
            else
            {
                builder.Append(c);
            }
        }

        return false;
    }
}
