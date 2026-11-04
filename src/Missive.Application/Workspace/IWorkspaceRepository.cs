namespace Missive.Application.Workspace;

/// <summary>Access to workspace folders on disk.</summary>
public interface IWorkspaceRepository
{
    /// <summary>Whether <paramref name="workspacePath"/> is an existing folder.</summary>
    Task<bool> ExistsAsync(string workspacePath, CancellationToken cancellationToken = default);
}
