using Dynpost.Application.Common;
using Dynpost.Application.Requests;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Infrastructure.Yaml;

/// <summary><see cref="IRequestFileFormat"/> for the YAML request files.</summary>
public sealed class YamlRequestFileFormat : IRequestFileFormat
{
    public Result<HttpRequestDefinition> Parse(string text, WorkspacePath path)
    {
        try
        {
            return RequestYamlSerializer.Deserialize(text, WorkspaceLayout.FileStem(path.FileName));
        }
        catch (RequestFormatException ex)
        {
            return new Error(ErrorCodes.InvalidFile, $"'{path}' is not a valid request file. {ex.Message}");
        }
    }
}
