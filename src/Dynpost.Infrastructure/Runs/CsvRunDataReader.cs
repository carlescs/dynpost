using System.Text;
using Dynpost.Application.Common;
using Dynpost.Application.Runs;
using Dynpost.Domain.Workspaces;
using static Dynpost.Infrastructure.Workspaces.WorkspaceFiles;

namespace Dynpost.Infrastructure.Runs;

/// <summary>Reads CSV data files (RFC 4180: comma separated, quotes escaped by doubling) with a header row.</summary>
public sealed class CsvRunDataReader : IRunDataReader
{
    private const long MaxBytes = 20 * 1024 * 1024;

    public async Task<Result<IReadOnlyList<IReadOnlyDictionary<string, string>>>> ReadCsvAsync(
        string root,
        WorkspacePath path,
        CancellationToken cancellationToken = default)
    {
        string text;
        try
        {
            var fullPath = FullPath(root, path);
            if (!File.Exists(fullPath))
            {
                return new Error(ErrorCodes.NotFound, $"The data file '{path}' does not exist.");
            }

            if (new FileInfo(fullPath).Length > MaxBytes)
            {
                return new Error(ErrorCodes.InvalidFile, $"The data file '{path}' is larger than 20 MB.");
            }

            text = await File.ReadAllTextAsync(fullPath, Utf8NoBom, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read '{path}': {ex.Message}");
        }

        List<List<string>> records;
        try
        {
            records = Parse(text);
        }
        catch (FormatException ex)
        {
            return new Error(ErrorCodes.InvalidFile, $"The data file '{path}' is not valid CSV: {ex.Message}");
        }

        if (records.Count == 0)
        {
            return new Error(ErrorCodes.InvalidFile, $"The data file '{path}' is empty: it needs a header row.");
        }

        var header = records[0].Select(name => name.Trim()).ToList();
        if (header.Any(string.IsNullOrEmpty) || header.Distinct(StringComparer.Ordinal).Count() != header.Count)
        {
            return new Error(ErrorCodes.InvalidFile, $"The header row of '{path}' needs unique, non-empty variable names.");
        }

        var rows = new List<IReadOnlyDictionary<string, string>>();
        for (var i = 1; i < records.Count; i++)
        {
            if (records[i].Count != header.Count)
            {
                return new Error(ErrorCodes.InvalidFile, $"Row {i + 1} of '{path}' has {records[i].Count} values; the header has {header.Count}.");
            }

            rows.Add(header.Zip(records[i]).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal));
        }

        return rows;
    }

    internal static List<List<string>> Parse(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var any = false;
        if (text.StartsWith('\uFEFF'))
        {
            text = text[1..];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    any = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    any = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    field.Append(c);
                    any = true;
                    break;
            }
        }

        if (quoted)
        {
            throw new FormatException("a quoted field is not terminated");
        }

        EndRecord();
        return records;

        void EndRecord()
        {
            if (any || record.Count > 0)
            {
                record.Add(field.ToString());
                records.Add(record);
            }

            record = [];
            field.Clear();
            any = false;
        }
    }
}
