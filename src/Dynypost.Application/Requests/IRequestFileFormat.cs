using Dynypost.Application.Common;
using Dynypost.Domain.Requests;
using Dynypost.Domain.Workspaces;

namespace Dynypost.Application.Requests;

/// <summary>Reads the text of a request file that is not (or not only) on disk, such as a committed version.</summary>
public interface IRequestFileFormat
{
    /// <summary>
    /// Parses the text of the request file at <paramref name="path"/>, or says why it is not a valid
    /// request file (<see cref="ErrorCodes.InvalidFile"/>).
    /// </summary>
    Result<HttpRequestDefinition> Parse(string text, WorkspacePath path);
}
