using System.Text.Json;

namespace Missive.Desktop.Bridge;

/// <summary>
/// Routes a serialized <see cref="BridgeRequest"/> to its handler and serializes the
/// <see cref="BridgeResponse"/>. Holds no business logic.
/// </summary>
internal sealed class BridgeDispatcher
{
    private readonly Dictionary<string, IBridgeHandler> _handlers;

    public BridgeDispatcher(IEnumerable<IBridgeHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Type, StringComparer.Ordinal);
    }

    /// <summary>
    /// Handles one message. Returns the serialized response, or <c>null</c> when the message is not
    /// a valid request envelope (there is no id to reply to).
    /// </summary>
    public async Task<string?> DispatchAsync(string message, CancellationToken cancellationToken = default)
    {
        BridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(message, BridgeJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (request is null || string.IsNullOrEmpty(request.Id))
        {
            return null;
        }

        var response = await HandleAsync(request, cancellationToken);
        return JsonSerializer.Serialize(response, BridgeJson.Options);
    }

    private async Task<BridgeResponse> HandleAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(request.Type, out var handler))
        {
            return BridgeResponse.Failure(request.Id, BridgeErrors.UnknownType(request.Type));
        }

        try
        {
            var result = await handler.HandleAsync(request.Payload, cancellationToken);
            return result.IsSuccess
                ? BridgeResponse.Success(request.Id, result.Value)
                : BridgeResponse.Failure(request.Id, result.Error!);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BridgeResponse.Failure(request.Id, BridgeErrors.InternalError(ex.Message));
        }
    }
}
