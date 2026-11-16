namespace Dynpost.Application.Interchange;

/// <summary>
/// Turns another app's CSV data file into the shape run plans read: comma-separated, with a header
/// line naming the variables, LF line endings and a trailing newline.
/// </summary>
internal static class CsvConversion
{
    /// <summary>
    /// The converted text, or <c>null</c> when a file with another delimiter quotes its values
    /// (splitting it would need a full CSV parser for that dialect).
    /// </summary>
    public static string? Convert(string text, ImportedDataFile data)
    {
        var lines = text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (data.SkipFirstLine && lines.Count > 0)
        {
            lines.RemoveAt(0);
        }

        if (data.Delimiter != ",")
        {
            if (data.Delimiter.Length == 0 || lines.Any(line => line.Contains('"')))
            {
                return null;
            }

            lines = lines
                .Select(line => string.Join(',', line.Split(data.Delimiter).Select(Quote)))
                .ToList();
        }

        if (data.Header is not null)
        {
            lines.Insert(0, data.Header);
        }

        return lines.Count == 0 ? string.Empty : string.Join('\n', lines) + "\n";
    }

    /// <summary>A value as a CSV field: quoted when it holds a comma.</summary>
    public static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
