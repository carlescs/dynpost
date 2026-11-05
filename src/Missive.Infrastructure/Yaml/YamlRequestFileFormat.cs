using Missive.Application.Common;
using Missive.Application.Requests;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;

namespace Missive.Infrastructure.Yaml;

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
