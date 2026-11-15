namespace Dynpost.Application.Search;

/// <summary>A request quick open offers, best match first.</summary>
/// <param name="Name">The request's name; the file name when the file could not be read.</param>
/// <param name="Method">The HTTP method; <c>null</c> when the file could not be read.</param>
/// <param name="Url">The URL as written, variables unresolved; empty when the file could not be read.</param>
/// <param name="NameMatches">Indexes of the characters of <paramref name="Name"/> the query matched.</param>
/// <param name="PathMatches">Indexes of the characters of <paramref name="Path"/> the query matched.</param>
/// <param name="UrlMatches">Indexes of the characters of <paramref name="Url"/> the query matched.</param>
public sealed record QuickOpenItemDto(
    string Path,
    string Name,
    string? Method,
    string Url,
    IReadOnlyList<int> NameMatches,
    IReadOnlyList<int> PathMatches,
    IReadOnlyList<int> UrlMatches);

/// <summary>Where a match is in a <see cref="SearchLineDto.Text"/>.</summary>
public sealed record SearchRangeDto(int Start, int Length);

/// <summary>A line of a request file containing the searched text.</summary>
/// <param name="Line">The line's number, from 1.</param>
/// <param name="Text">The line without its indentation, cut around the match when it is long.</param>
public sealed record SearchLineDto(int Line, string Text, IReadOnlyList<SearchRangeDto> Ranges);

/// <summary>A request file with matches.</summary>
/// <param name="Name">The request's name; the file name when the file could not be read.</param>
/// <param name="Method">The HTTP method; <c>null</c> when the file could not be read.</param>
public sealed record SearchFileDto(string Path, string Name, string? Method, IReadOnlyList<SearchLineDto> Lines);

/// <summary>What a workspace search found, grouped by file: in the tree's order, or closest matches first for a fuzzy search.</summary>
/// <param name="MatchCount">How many matches the files listed contain.</param>
/// <param name="Truncated">Whether the search stopped at <see cref="SearchWorkspace.MaxLines"/> matching lines.</param>
public sealed record WorkspaceSearchDto(IReadOnlyList<SearchFileDto> Files, int MatchCount, bool Truncated);
