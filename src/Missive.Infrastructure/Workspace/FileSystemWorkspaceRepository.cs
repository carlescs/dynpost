using Missive.Application.Workspace;

namespace Missive.Infrastructure.Workspace;

/// <summary><see cref="IWorkspaceRepository"/> over the local file system.</summary>
public sealed class FileSystemWorkspaceRepository : IWorkspaceRepository
{
    public Task<bool> ExistsAsync(string workspacePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Directory.Exists(workspacePath));
}
