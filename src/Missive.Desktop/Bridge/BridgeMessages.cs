using System.Text.Json;
using Missive.Application.Common;

namespace Missive.Desktop.Bridge;

/// <summary>
/// A message from the UI: <c>{ "id": "1", "type": "git.status", "payload": { ... } }</c>. The id
/// correlates the response. Keep in sync with <c>Missive.Client/src/app/bridge/messages.ts</c>.
/// </summary>
internal sealed record BridgeRequest(string Id, string Type, JsonElement? Payload = null);

/// <summary>
/// The reply to a <see cref="BridgeRequest"/>: <c>{ "id", "ok": true, "payload" }</c> or
/// <c>{ "id", "ok": false, "error": { "code", "message" } }</c>.
/// </summary>
internal sealed record BridgeResponse(string Id, bool Ok, object? Payload = null, Error? Error = null)
{
    public static BridgeResponse Success(string id, object? payload) => new(id, true, payload);

    public static BridgeResponse Failure(string id, Error error) => new(id, false, Error: error);
}

/// <summary>Message type names. Keep in sync with the client's <c>messages.ts</c>.</summary>
internal static class BridgeMessageTypes
{
    public const string WorkspacePickFolder = "workspace.pickFolder";
    public const string WorkspaceOpen = "workspace.open";
    public const string WorkspaceGetLast = "workspace.getLast";
    public const string GitStatus = "git.status";
    public const string GitCommit = "git.commit";
    public const string GitPull = "git.pull";
    public const string GitPush = "git.push";
}

/// <summary>Errors produced by the bridge itself rather than by a use case.</summary>
internal static class BridgeErrors
{
    public const string UnknownTypeCode = "bridge.unknown_type";
    public const string InvalidPayloadCode = "bridge.invalid_payload";
    public const string InternalErrorCode = "bridge.internal_error";

    public static Error UnknownType(string type) =>
        new(UnknownTypeCode, $"Unknown message type '{type}'.");

    public static Error InvalidPayload(string type, string details) =>
        new(InvalidPayloadCode, $"Invalid payload for '{type}': {details}");

    public static Error InternalError(string details) =>
        new(InternalErrorCode, $"Unexpected error: {details}");
}
