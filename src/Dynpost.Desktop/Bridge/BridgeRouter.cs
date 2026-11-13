using System.Diagnostics;
using System.Text.Json;
using Dynpost.Application.Common;

namespace Dynpost.Desktop.Bridge;

/// <summary>
/// Dispatches bridge messages to their handlers and turns results into response envelopes.
/// Handlers only deserialize their payload, call one use case and return its result.
/// </summary>
internal sealed class BridgeRouter
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<Result<object?>>>> _handlers = new(StringComparer.Ordinal);

    public BridgeRouter Register<TPayload, TResult>(string type, Func<TPayload, CancellationToken, Task<Result<TResult>>> handler)
    {
        _handlers.Add(type, async (payload, cancellationToken) =>
        {
            TPayload? typed;
            try
            {
                typed = payload is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } element
                    ? element.Deserialize<TPayload>(JsonOptions)
                    : default;
            }
            catch (JsonException ex)
            {
                return new Error(BridgeErrorCodes.BadPayload, $"Invalid payload for '{type}': {ex.Message}");
            }

            if (typed is null)
            {
                return new Error(BridgeErrorCodes.BadPayload, $"'{type}' needs a payload.");
            }

            return Box(await handler(typed, cancellationToken));
        });
        return this;
    }

    public BridgeRouter Register<TResult>(string type, Func<CancellationToken, Task<Result<TResult>>> handler)
    {
        _handlers.Add(type, async (_, cancellationToken) => Box(await handler(cancellationToken)));
        return this;
    }

    /// <summary>Handles one raw message and returns the serialized response, or null if the message has no id.</summary>
    public async Task<string?> HandleAsync(string message, CancellationToken cancellationToken = default)
    {
        BridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(message, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (request is null || string.IsNullOrEmpty(request.Id))
        {
            return null;
        }

        BridgeResponse response;
        if (!_handlers.TryGetValue(request.Type ?? string.Empty, out var handler))
        {
            response = Fail(request.Id, new Error(BridgeErrorCodes.UnknownType, $"Unknown message type '{request.Type}'."));
        }
        else
        {
            try
            {
                var result = await handler(request.Payload, cancellationToken);
                response = result.IsSuccess ? new BridgeResponse(request.Id, true, result.Value) : Fail(request.Id, result.Error!);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Bridge handler '{request.Type}' failed: {ex}");
                response = Fail(request.Id, new Error(BridgeErrorCodes.Internal, ex.Message));
            }
        }

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static Result<object?> Box<T>(Result<T> result) =>
        result.IsSuccess ? Result<object?>.Success(result.Value) : Result<object?>.Failure(result.Error!);

    private static BridgeResponse Fail(string id, Error error) => new(id, false, Error: error);
}
