using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Search;

namespace Dynpost.Application.Search;

/// <summary>
/// Searches the text of every request file — URLs, parameters, headers, bodies, descriptions —
/// and returns the matching lines grouped by file.
/// </summary>
public sealed class SearchWorkspace(IWorkspaceRepository repository, CurrentWorkspace current)
{
    /// <summary>The search stops after this many matching lines, so a one-letter query stays quick to show.</summary>
    public const int MaxLines = 1000;

    public async Task<Result<WorkspaceSearchDto>> ExecuteAsync(string? query, bool matchCase = false, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        var files = await RequestFiles.ListAsync(repository, root.Value, cancellationToken);
        if (!files.IsSuccess)
        {
            return files.Error!;
        }

        if (string.IsNullOrEmpty(query))
        {
            return new WorkspaceSearchDto([], 0, false);
        }

        var found = new List<SearchFileDto>();
        var lineCount = 0;
        var matchCount = 0;
        var truncated = false;
        foreach (var file in files.Value)
        {
            var text = await repository.ReadTextAsync(root.Value, file.Path, cancellationToken);
            if (!text.IsSuccess)
            {
                // Deleted since it was listed, or unreadable: there is nothing to search in it.
                continue;
            }

            var lines = TextSearch.Find(text.Value, query, matchCase);
            if (lines.Count == 0)
            {
                continue;
            }

            if (lineCount + lines.Count > MaxLines)
            {
                truncated = true;
                if (lineCount == MaxLines)
                {
                    break;
                }

                lines = [.. lines.Take(MaxLines - lineCount)];
            }

            lineCount += lines.Count;
            matchCount += lines.Sum(line => line.Ranges.Count);
            found.Add(new SearchFileDto(
                file.Path.Value,
                file.Name,
                file.Method,
                [.. lines.Select(line => new SearchLineDto(line.Line, line.Text, [.. line.Ranges.Select(r => new SearchRangeDto(r.Start, r.Length))]))]));
            if (truncated)
            {
                break;
            }
        }

        return new WorkspaceSearchDto(found, matchCount, truncated);
    }
}
