using Missive.Application.Common;
using Missive.Domain.Git;

namespace Missive.Application.Git;

/// <summary>Reads the git state of a workspace for the source control view.</summary>
public sealed class GetGitStatus(IGitService git)
{
    public Task<Result<GitStatus>> ExecuteAsync(string workspacePath, CancellationToken cancellationToken = default) =>
        git.GetStatusAsync(workspacePath, cancellationToken);
}
