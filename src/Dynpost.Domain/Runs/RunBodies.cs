using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Domain.Runs;

/// <summary>
/// One file of a body folder. <see cref="Text"/> holds its content for requests that send text
/// bodies; it is <c>null</c> for <see cref="BodyType.Binary"/> requests, which send the file as is.
/// </summary>
public sealed record RunBodyFile(WorkspacePath Path, string? Text);

/// <summary>Rules for replacing a request's body with a file of a body folder.</summary>
public static class RunBodies
{
    /// <summary>Why a request with this body type can't take its body from a folder; <c>null</c> when it can.</summary>
    public static string? Unsupported(BodyType type) => type switch
    {
        BodyType.None => "has no body to replace: give it a body type (such as JSON) first",
        BodyType.Form => "sends a multipart form, which can't take its body from a folder yet",
        _ => null,
    };

    /// <summary>Whether the files are sent as text (read once, then <c>{{variable}}</c> resolved) rather than as is.</summary>
    public static bool UsesText(BodyType type) => type != BodyType.Binary;

    /// <summary>
    /// The request's body with its content taken from <paramref name="file"/>: the file's text
    /// (variables are resolved when the request is prepared) or, for a binary body, its path.
    /// The body type, and so the Content-Type, stays the request's. A <c>urlencoded</c> body's
    /// fields are replaced too: the file's text is sent as written.
    /// </summary>
    public static RequestBody Replace(RequestBody body, RunBodyFile file) =>
        body with { Content = body.Type == BodyType.Binary ? file.Path.Value : file.Text ?? string.Empty, Fields = null };
}
