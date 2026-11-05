using Missive.Application.Common;
using Missive.Application.Folders;
using Missive.Application.Git;
using Missive.Application.Http;
using Missive.Domain.Git;
using Missive.Application.Requests;
using Missive.Application.Updates;
using Missive.Application.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace Missive.Desktop.Bridge;

/// <summary>Maps each bridge message type to the use case that handles it.</summary>
internal static class BridgeHandlers
{
    public static BridgeRouter Create(IServiceProvider services, IFolderPicker folderPicker)
    {
        T Use<T>() where T : notnull => services.GetRequiredService<T>();

        return new BridgeRouter()
            .Register(BridgeMessageTypes.WorkspacePick, async ct =>
            {
                var folder = await folderPicker.PickFolderAsync("Open workspace folder");
                if (folder is null)
                {
                    return Result<WorkspaceDto?>.Success(null);
                }

                var opened = await Use<OpenWorkspace>().ExecuteAsync(folder, ct);
                return opened.IsSuccess ? opened.Value : Result<WorkspaceDto?>.Failure(opened.Error!);
            })
            .Register<OpenWorkspacePayload, WorkspaceDto>(BridgeMessageTypes.WorkspaceOpen,
                (p, ct) => Use<OpenWorkspace>().ExecuteAsync(p.Path, ct))
            .Register(BridgeMessageTypes.WorkspaceOpenLast,
                ct => Use<OpenLastWorkspace>().ExecuteAsync(ct))
            .Register(BridgeMessageTypes.WorkspaceTree,
                ct => Use<GetWorkspaceTree>().ExecuteAsync(ct))
            .Register<LoadRequestPayload, RequestDto>(BridgeMessageTypes.RequestLoad,
                (p, ct) => Use<LoadRequest>().ExecuteAsync(p.Path, ct))
            .Register<CreateRequestPayload, SavedRequestDto>(BridgeMessageTypes.RequestCreate,
                (p, ct) => Use<CreateRequest>().ExecuteAsync(p.Folder, p.Request, ct))
            .Register<SaveRequestPayload, SavedRequestDto>(BridgeMessageTypes.RequestSave,
                (p, ct) => Use<SaveRequest>().ExecuteAsync(p.Path, p.Request, ct))
            .Register<MoveRequestPayload, MovedRequestDto>(BridgeMessageTypes.RequestMove,
                (p, _) => Task.FromResult(Use<MoveRequest>().Execute(p.Path, p.Folder)))
            .Register<SendRequestPayload, ResponseDto>(BridgeMessageTypes.RequestSend,
                (p, ct) => Use<SendRequest>().ExecuteAsync(p.Id, p.Request, ct))
            .Register<CancelRequestPayload, bool>(BridgeMessageTypes.RequestCancel,
                (p, _) => Task.FromResult(Result<bool>.Success(Use<CancelRequest>().Execute(p.Id))))
            .Register<CreateFolderPayload, FolderDto>(BridgeMessageTypes.FolderCreate,
                (p, _) => Task.FromResult(Use<CreateFolder>().Execute(p.Parent, p.Name)))
            .Register<RenameFolderPayload, FolderDto>(BridgeMessageTypes.FolderRename,
                (p, _) => Task.FromResult(Use<RenameFolder>().Execute(p.Path, p.Name)))
            .Register<MoveFolderPayload, FolderDto>(BridgeMessageTypes.FolderMove,
                (p, _) => Task.FromResult(Use<MoveFolder>().Execute(p.Path, p.Parent)))
            .Register<DeleteFolderPayload, Unit?>(BridgeMessageTypes.FolderDelete,
                (p, _) => Task.FromResult(NoValue(Use<DeleteFolder>().Execute(p.Path))))
            .Register(BridgeMessageTypes.GitStatus,
                async ct => (await Use<GetGitStatus>().ExecuteAsync(ct)).Map(GitStatusDto.From))
            .Register<GitCommitPayload, GitCommitDto>(BridgeMessageTypes.GitCommit,
                async (p, ct) => (await Use<CommitChanges>().ExecuteAsync(new CommitChangesRequest(p.Message, p.Paths), ct)).Map(GitCommitDto.From))
            .Register(BridgeMessageTypes.GitPull,
                async ct => (await Use<PullChanges>().ExecuteAsync(ct)).Map(GitPullDto.From))
            .Register(BridgeMessageTypes.GitPush,
                async ct => (await Use<PushChanges>().ExecuteAsync(ct)).Map(GitStatusDto.From))
            .Register<GitConflictPayload, GitConflictDto>(BridgeMessageTypes.GitConflict,
                async (p, ct) => (await Use<GetConflict>().ExecuteAsync(p.Path, ct)).Map(GitConflictDto.From))
            .Register<GitResolvePayload, GitStatusDto>(BridgeMessageTypes.GitResolve,
                async (p, ct) => ParseResolution(p.Resolution) is { } resolution
                    ? (await Use<ResolveConflict>().ExecuteAsync(new ResolveConflictRequest(p.Path, resolution), ct)).Map(GitStatusDto.From)
                    : new Error(BridgeErrorCodes.BadPayload, $"Unknown resolution '{p.Resolution}'; use ours, theirs or edited."))
            .Register(BridgeMessageTypes.GitAbortMerge,
                async ct => (await Use<AbortMerge>().ExecuteAsync(ct)).Map(GitStatusDto.From))
            .Register(BridgeMessageTypes.UpdateCheck,
                ct => Use<CheckForUpdate>().ExecuteAsync(ct))
            .Register(BridgeMessageTypes.UpdateRestart,
                _ => Task.FromResult(NoValue(Use<RestartToUpdate>().Execute())));
    }

    private static ConflictResolution? ParseResolution(string? value) =>
        value switch
        {
            "ours" => ConflictResolution.Ours,
            "theirs" => ConflictResolution.Theirs,
            "edited" => ConflictResolution.Edited,
            _ => null,
        };

    /// <summary>Results without a value are sent as a <c>null</c> payload.</summary>
    private static Result<Unit?> NoValue(Result<Unit> result) =>
        result.IsSuccess ? Result<Unit?>.Success(null) : Result<Unit?>.Failure(result.Error!);
}
