using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Application.Runs;
using Missive.Domain.Workspaces;

namespace Missive.Application.Workspaces;

/// <summary>A file of the workspace the UI can open in a tab.</summary>
/// <param name="Kind"><c>request</c>, <c>runPlan</c> or <c>environment</c>.</param>
/// <param name="Id">The environment's id, for environments; otherwise the path.</param>
public sealed record WorkspaceItemDto(string Path, string Kind, string Id);

/// <summary>
/// Tells what a workspace path is — a request, a run plan or an environment — once it has checked
/// that the file exists, so the UI can open it in a tab.
/// </summary>
public sealed class FindWorkspaceItem(
    IWorkspaceRepository repository,
    IRunPlanStore plans,
    IEnvironmentStore environments,
    CurrentWorkspace current)
{
    public const string Request = "request";
    public const string RunPlan = "runPlan";
    public const string Environment = "environment";

    public Result<WorkspaceItemDto> Execute(string path)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!WorkspacePath.TryParse(path, out var parsed) || parsed.IsRoot)
        {
            return Invalid(path);
        }

        var (kind, exists) = parsed switch
        {
            _ when WorkspaceLayout.IsRunPlanPath(parsed) => (RunPlan, plans.Exists(root.Value, parsed)),
            _ when WorkspaceLayout.IsRequestPath(parsed) => (Request, repository.FileExists(root.Value, parsed)),
            _ when parsed.Parent.Value == WorkspaceLayout.EnvironmentsFolder && EnvironmentMapper.IsValidId(parsed.FileName)
                => (Environment, environments.Exists(root.Value, parsed.FileName)),
            _ => (null, false),
        };

        if (kind is null)
        {
            return Invalid(path);
        }

        return exists
            ? new WorkspaceItemDto(parsed.Value, kind, kind == Environment ? parsed.FileName : parsed.Value)
            : new Error(ErrorCodes.NotFound, $"'{path}' does not exist.");
    }

    private static Error Invalid(string path) =>
        new(ErrorCodes.InvalidPath, $"'{path}' is not a request, run plan or environment file of the workspace.");
}
