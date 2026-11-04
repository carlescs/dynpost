using System.Text.Json;
using Missive.Application.Common;

namespace Missive.Desktop.Bridge;

/// <summary>Handles one bridge message type.</summary>
internal interface IBridgeHandler
{
    string Type { get; }

    /// <summary>Returns the response payload, or the error to report.</summary>
    Task<Result<object?>> HandleAsync(JsonElement? payload, CancellationToken cancellationToken);
}

/// <summary>
/// Base for handlers with a typed payload: deserializes it, calls <see cref="HandleAsync(TRequest, CancellationToken)"/>
/// (which should invoke one use case) and returns its result.
/// </summary>
internal abstract class BridgeHandler<TRequest, TResponse> : IBridgeHandler
    where TRequest : class
{
    public abstract string Type { get; }

    protected abstract Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken);

    async Task<Result<object?>> IBridgeHandler.HandleAsync(JsonElement? payload, CancellationToken cancellationToken)
    {
        TRequest? request;
        try
        {
            request = payload is { ValueKind: not JsonValueKind.Null } json
                ? json.Deserialize<TRequest>(BridgeJson.Options)
                : null;
        }
        catch (JsonException ex)
        {
            return Result<object?>.Failure(BridgeErrors.InvalidPayload(Type, ex.Message));
        }

        if (request is null)
        {
            return Result<object?>.Failure(BridgeErrors.InvalidPayload(Type, "the payload is missing."));
        }

        var result = await HandleAsync(request, cancellationToken);
        return result.Map<object?>(value => value);
    }
}
