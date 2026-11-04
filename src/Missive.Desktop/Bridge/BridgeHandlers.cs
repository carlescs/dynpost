using Missive.Application.Common;
using Missive.Application.Requests;
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
            .Register(BridgeMessageTypes.WorkspaceTree,
                ct => Use<GetWorkspaceTree>().ExecuteAsync(ct))
            .Register<LoadRequestPayload, RequestDto>(BridgeMessageTypes.RequestLoad,
                (p, ct) => Use<LoadRequest>().ExecuteAsync(p.Path, ct))
            .Register<CreateRequestPayload, SavedRequestDto>(BridgeMessageTypes.RequestCreate,
                (p, ct) => Use<CreateRequest>().ExecuteAsync(p.Folder, p.Request, ct))
            .Register<SaveRequestPayload, SavedRequestDto>(BridgeMessageTypes.RequestSave,
                (p, ct) => Use<SaveRequest>().ExecuteAsync(p.Path, p.Request, ct));
    }
}
