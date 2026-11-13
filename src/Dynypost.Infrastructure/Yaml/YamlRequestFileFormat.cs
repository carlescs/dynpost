using Dynypost.Application.Common;
using Dynypost.Application.Requests;
using Dynypost.Domain.Requests;
using Dynypost.Domain.Workspaces;

namespace Dynypost.Infrastructure.Yaml;

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
