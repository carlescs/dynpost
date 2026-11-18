using Dynpost.Application.Common;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Application.Requests;

/// <summary>
/// Reads and writes the text of a request file that is not (or not only) on disk, such as a committed
/// version or the request in an editor with unsaved changes.
/// </summary>
public interface IRequestFileFormat
{
    /// <summary>
    /// Parses the text of the request file at <paramref name="path"/>, or says why it is not a valid
    /// request file (<see cref="ErrorCodes.InvalidFile"/>).
    /// </summary>
    Result<HttpRequestDefinition> Parse(string text, WorkspacePath path);

    /// <summary>The text a request file holding <paramref name="request"/> is saved with.</summary>
    string Format(HttpRequestDefinition request);
}
