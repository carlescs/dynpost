using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Missive.Infrastructure.Yaml;

/// <summary>
/// A tiny block-style YAML writer with deterministic output: 2-space indent, LF line endings,
/// plain scalars where they are unambiguous strings, double quotes otherwise, and literal blocks
/// (<c>|</c>) for multi-line text. It only writes what Missive's files need — nested mappings of
/// string scalars and sequences of single-line strings — so the output is predictable and diffs stay small.
/// </summary>
internal sealed partial class YamlWriter
{
    private const int IndentSize = 2;

    private readonly StringBuilder _builder = new();
    private int _depth;

    /// <summary>Writes <c>key: value</c>.</summary>
    public YamlWriter Scalar(string key, string value)
    {
        Indent();
        _builder.Append(FormatKey(key)).Append(':');

        if (CanUseLiteralBlock(value))
        {
            WriteLiteralBlock(value);
        }
        else
        {
            _builder.Append(' ').Append(FormatScalar(value)).Append('\n');
        }

        return this;
    }

    /// <summary>Writes <c>key: true</c> or <c>key: false</c> (an unquoted YAML boolean).</summary>
    public YamlWriter Boolean(string key, bool value)
    {
        Indent();
        _builder.Append(FormatKey(key)).Append(": ").Append(value ? "true" : "false").Append('\n');
        return this;
    }

    /// <summary>Writes <c>key:</c> and indents what <paramref name="body"/> writes under it.</summary>
    public YamlWriter Mapping(string key, Action<YamlWriter> body)
    {
        Indent();
        _builder.Append(FormatKey(key)).Append(":\n");
        _depth++;
        body(this);
        _depth--;
        return this;
    }

    /// <summary>Writes <c>key:</c> followed by one <c>- item</c> line per item.</summary>
    public YamlWriter Sequence(string key, IEnumerable<string> items)
    {
        Indent();
        _builder.Append(FormatKey(key)).Append(":\n");
        _depth++;
        foreach (var item in items)
        {
            Indent();
            _builder.Append("- ").Append(FormatScalar(item)).Append('\n');
        }

        _depth--;
        return this;
    }

    public override string ToString() => _builder.ToString();

    private void Indent() => _builder.Append(' ', _depth * IndentSize);

    private void WriteLiteralBlock(string value)
    {
        var trailingNewlines = value.Length - value.TrimEnd('\n').Length;
        var chomping = trailingNewlines switch
        {
            0 => "-",
            1 => string.Empty,
            _ => "+",
        };

        _builder.Append(" |").Append(chomping).Append('\n');

        var content = trailingNewlines == 0 ? value : value[..^1];
        var indent = new string(' ', (_depth + 1) * IndentSize);
        foreach (var line in content.Split('\n'))
        {
            if (line.Length > 0)
            {
                _builder.Append(indent).Append(line);
            }

            _builder.Append('\n');
        }
    }

    private static string FormatKey(string key) => IsPlainSafe(key) ? key : Quote(key);

    private static string FormatScalar(string value) => IsPlainSafe(value) ? value : Quote(value);

    /// <summary>
    /// Literal blocks keep multi-line text readable. They are only used when they round-trip
    /// exactly: LF-only text whose first line is not empty and does not start with whitespace
    /// (otherwise the block's indentation would be ambiguous).
    /// </summary>
    private static bool CanUseLiteralBlock(string value) =>
        value.Contains('\n')
        && value.Length > 0
        && value[0] is not ('\n' or ' ' or '\t')
        && !value.Any(c => c != '\n' && c != '\t' && char.IsControl(c))
        && !value.Contains('﻿');

    /// <summary>
    /// Whether a scalar can be written unquoted and still read back as the same string by any
    /// YAML 1.1 or 1.2 parser. Deliberately conservative: when in doubt, quote.
    /// </summary>
    private static bool IsPlainSafe(string value)
    {
        if (value.Length == 0 || value != value.Trim() || value.Any(char.IsControl) || value.Contains('﻿'))
        {
            return false;
        }

        if ("-?:,[]{}#&*!|>'\"%@`".Contains(value[0]))
        {
            return false;
        }

        if (value.Contains(": ") || value.Contains(" #") || value.EndsWith(':'))
        {
            return false;
        }

        return !NonStringScalar().IsMatch(value);
    }

    /// <summary>Plain scalars that YAML 1.1/1.2 resolve to nulls, booleans, numbers or dates.</summary>
    [GeneratedRegex(
        @"^(~|null|true|false|yes|no|on|off|y|n|[-+.]?\d.*|\.(inf|nan)|[-+]\.inf|0[box].*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonStringScalar();

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\0': builder.Append("\\0"); break;
                default:
                    if (char.IsControl(c) || c == '﻿')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
