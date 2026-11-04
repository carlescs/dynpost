using Missive.Application.Common;
using Missive.Application.Git;

namespace Missive.Desktop.Bridge.Git;

internal sealed class GitStatusHandler(GetGitStatus useCase) : BridgeHandler<WorkspaceRequest, GitStatusDto>
{
    public override string Type => BridgeMessageTypes.GitStatus;

    protected override async Task<Result<GitStatusDto>> HandleAsync(
        WorkspaceRequest request,
        CancellationToken cancellationToken) =>
        (await useCase.ExecuteAsync(request.WorkspacePath, cancellationToken)).Map(GitStatusDto.From);
}

internal sealed class GitCommitHandler(CommitChanges useCase) : BridgeHandler<GitCommitRequest, GitCommitResponse>
{
    public override string Type => BridgeMessageTypes.GitCommit;

    protected override async Task<Result<GitCommitResponse>> HandleAsync(
        GitCommitRequest request,
        CancellationToken cancellationToken) =>
        (await useCase.ExecuteAsync(
            new CommitChangesRequest(request.WorkspacePath, request.Message, request.Paths),
            cancellationToken))
        .Map(r => new GitCommitResponse(r.CommitId, GitStatusDto.From(r.Status)));
}

internal sealed class GitPullHandler(PullChanges useCase) : BridgeHandler<WorkspaceRequest, GitPullResponse>
{
    public override string Type => BridgeMessageTypes.GitPull;

    protected override async Task<Result<GitPullResponse>> HandleAsync(
        WorkspaceRequest request,
        CancellationToken cancellationToken) =>
        (await useCase.ExecuteAsync(request.WorkspacePath, cancellationToken))
        .Map(r => new GitPullResponse(r.Outcome, GitStatusDto.From(r.Status)));
}

internal sealed class GitPushHandler(PushChanges useCase) : BridgeHandler<WorkspaceRequest, GitStatusDto>
{
    public override string Type => BridgeMessageTypes.GitPush;

    protected override async Task<Result<GitStatusDto>> HandleAsync(
        WorkspaceRequest request,
        CancellationToken cancellationToken) =>
        (await useCase.ExecuteAsync(request.WorkspacePath, cancellationToken)).Map(GitStatusDto.From);
}
