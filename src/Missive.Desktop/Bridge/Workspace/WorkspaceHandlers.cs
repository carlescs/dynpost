using Missive.Application.Common;
using Missive.Application.Workspace;
using Missive.Desktop.Bridge.Git;

namespace Missive.Desktop.Bridge.Workspace;

// Wire format of the workspace messages. Keep in sync with Missive.Client/src/app/bridge/messages.ts.

internal sealed record EmptyRequest
{
}

internal sealed record WorkspaceDto(string Path, string Name)
{
    public static WorkspaceDto From(WorkspaceInfo workspace) => new(workspace.Path, workspace.Name);
}

/// <param name="Workspace">The workspace to reopen, or <c>null</c> when there is none.</param>
internal sealed record LastWorkspaceResponse(WorkspaceDto? Workspace);

internal sealed class OpenWorkspaceHandler(OpenWorkspace useCase) : BridgeHandler<WorkspaceRequest, WorkspaceDto>
{
    public override string Type => BridgeMessageTypes.WorkspaceOpen;

    protected override async Task<Result<WorkspaceDto>> HandleAsync(
        WorkspaceRequest request,
        CancellationToken cancellationToken) =>
        (await useCase.ExecuteAsync(request.WorkspacePath, cancellationToken)).Map(WorkspaceDto.From);
}

internal sealed class GetLastWorkspaceHandler(GetLastWorkspace useCase) : BridgeHandler<EmptyRequest, LastWorkspaceResponse>
{
    public override string Type => BridgeMessageTypes.WorkspaceGetLast;

    protected override async Task<Result<LastWorkspaceResponse>> HandleAsync(
        EmptyRequest request,
        CancellationToken cancellationToken) =>
        (await useCase.ExecuteAsync(cancellationToken))
        .Map(r => new LastWorkspaceResponse(r.Workspace is { } w ? WorkspaceDto.From(w) : null));
}
