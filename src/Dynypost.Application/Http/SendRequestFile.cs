using Dynypost.Application.Common;
using Dynypost.Application.Requests;

namespace Dynypost.Application.Http;

/// <summary>
/// Sends a saved request as its file says, with the selected environment, like sending it
/// unchanged from the editor: its response is kept in the request's history.
/// </summary>
public sealed class SendRequestFile(LoadRequest load, SendRequest send)
{
    /// <param name="id">An id chosen by the caller, used to cancel the request with <see cref="CancelRequest"/>.</param>
    public async Task<Result<ResponseDto>> ExecuteAsync(string id, string path, CancellationToken cancellationToken = default)
    {
        var request = await load.ExecuteAsync(path, cancellationToken);
        return request.IsSuccess
            ? await send.ExecuteAsync(id, request.Value, path, cancellationToken: cancellationToken)
            : request.Error!;
    }
}
