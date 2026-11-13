using System.Text.RegularExpressions;

namespace Dynypost.Application.Runs;

/// <summary>
/// Names the files a run saves its responses to (when its plan has <c>saveResponses</c>): the
/// response's number in the run, with an extension that matches its media type (<c>12.json</c>).
/// </summary>
public static partial class RunResponseFiles
{
    public const string PdfMediaType = "application/pdf";

    public static string Name(int number, string? mediaType) => $"{number}.{Extension(mediaType)}";

    /// <summary>Whether a text is shaped like a name from <see cref="Name"/>, so it is safe as a file name.</summary>
    public static bool IsValidName(string name) => NamePattern().IsMatch(name);

    public static string Extension(string? mediaType) => mediaType switch
    {
        PdfMediaType => "pdf",
        "application/json" or "text/json" => "json",
        "text/html" or "application/xhtml+xml" => "html",
        "application/xml" or "text/xml" => "xml",
        "text/csv" => "csv",
        "text/css" => "css",
        "text/javascript" or "application/javascript" => "js",
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/gif" => "gif",
        "image/webp" => "webp",
        "image/svg+xml" => "svg",
        "application/zip" => "zip",
        not null when mediaType.EndsWith("+json", StringComparison.Ordinal) => "json",
        not null when mediaType.EndsWith("+xml", StringComparison.Ordinal) => "xml",
        not null when mediaType.StartsWith("text/", StringComparison.Ordinal) => "txt",
        _ => "bin",
    };

    [GeneratedRegex("^[1-9][0-9]{0,9}\\.[a-z0-9]{1,8}$")]
    private static partial Regex NamePattern();
}
