using System.Text.Json;
using System.Text.Json.Serialization;

namespace Missive.Desktop.Bridge;

/// <summary>JSON settings for bridge messages: camelCase names and camelCase enum strings.</summary>
internal static class BridgeJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Reject payloads that omit required constructor parameters or send null for non-nullable ones.
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
