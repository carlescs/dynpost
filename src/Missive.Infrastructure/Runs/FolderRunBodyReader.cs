using System.IO.Enumeration;
using System.Text;
using Missive.Application.Common;
using Missive.Application.Runs;
using Missive.Domain.Runs;
using Missive.Domain.Workspaces;
using static Missive.Infrastructure.Workspaces.WorkspaceFiles;

namespace Missive.Infrastructure.Runs;

/// <summary>
/// Reads body folders of run plans: the files directly in the folder (hidden ones, whose names start
/// with a dot, are skipped), filtered by a <c>*</c>/<c>?</c> pattern matched without regard to case
/// and ordered ordinally by name so every machine picks them in the same order.
/// </summary>
public sealed class FolderRunBodyReader : IRunBodyReader
{
    internal const long MaxFileBytes = 10 * 1024 * 1024;
    internal const long MaxTotalBytes = 200 * 1024 * 1024;
    internal const int MaxFiles = 100_000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task<Result<IReadOnlyList<RunBodyFile>>> ReadFolderAsync(
        string root,
        WorkspacePath folder,
        string? pattern,
        bool readText,
        CancellationToken cancellationToken = default)
    {
        var shown = folder.IsRoot ? "the workspace folder" : $"the body folder '{folder}'";
        List<FileInfo> files;
        try
        {
            var fullPath = FullPath(root, folder);
            if (!Directory.Exists(fullPath))
            {
                return new Error(ErrorCodes.NotFound, $"The body folder '{folder}' does not exist.");
            }

            files = new DirectoryInfo(fullPath)
                .EnumerateFiles()
                .Where(file => !file.Name.StartsWith('.'))
                .Where(file => pattern is null || FileSystemName.MatchesSimpleExpression(pattern, file.Name, ignoreCase: true))
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .Take(MaxFiles + 1)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read {shown}: {ex.Message}");
        }

        if (files.Count == 0)
        {
            return new Error(
                ErrorCodes.InvalidFile,
                pattern is null ? $"{Capitalize(shown)} has no files." : $"{Capitalize(shown)} has no files that match '{pattern}'.");
        }

        if (files.Count > MaxFiles)
        {
            return new Error(ErrorCodes.InvalidFile, $"{Capitalize(shown)} has more than {MaxFiles:N0} files.");
        }

        var result = new List<RunBodyFile>(files.Count);
        long total = 0;
        foreach (var file in files)
        {
            var path = folder.Combine(file.Name);
            string? text = null;
            try
            {
                if (file.Length > MaxFileBytes)
                {
                    return new Error(ErrorCodes.InvalidFile, $"The body file '{path}' is larger than 10 MB.");
                }

                if (readText)
                {
                    total += file.Length;
                    if (total > MaxTotalBytes)
                    {
                        return new Error(ErrorCodes.InvalidFile, $"The files of {shown} add up to more than 200 MB.");
                    }

                    text = await File.ReadAllTextAsync(file.FullName, StrictUtf8, cancellationToken);
                }
                else if (!CanOpen(file))
                {
                    return new Error(ErrorCodes.Io, $"Could not read the body file '{path}'.");
                }
            }
            catch (DecoderFallbackException)
            {
                return new Error(ErrorCodes.InvalidFile, $"The body file '{path}' is not UTF-8 text.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new Error(ErrorCodes.Io, $"Could not read the body file '{path}': {ex.Message}");
            }

            result.Add(new RunBodyFile(path, text));
        }

        return result;
    }

    private static bool CanOpen(FileInfo file)
    {
        using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return stream.CanRead;
    }

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
