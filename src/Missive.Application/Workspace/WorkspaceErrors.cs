using Missive.Application.Common;

namespace Missive.Application.Workspace;

public static class WorkspaceErrors
{
    public const string NotFoundCode = "workspace.not_found";

    public static Error NotFound(string path) =>
        new(NotFoundCode, $"The workspace folder '{path}' does not exist.");
}
