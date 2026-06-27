using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alife.Function.WebBridge;

public static class AlifeManagementJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

public sealed record AlifeHealthResponse(
    string Status,
    string Service,
    string Version,
    DateTimeOffset TimestampUtc);

public sealed record AlifeRuntimeStatusResponse(
    string Status,
    string Agent,
    string OwnerId,
    string BotId,
    [property: JsonPropertyName("qchatEnabled")]
    bool QChatEnabled,
    bool VisionEnabled,
    string VisionStatus,
    string VisionReason,
    bool TtsEnabled,
    string TtsStatus,
    string TtsReason,
    bool OutboxEnabled,
    DateTimeOffset TimestampUtc);

public sealed record AlifeQChatStatusResponse(
    bool Enabled,
    string Agent,
    string OwnerId,
    string BotId,
    string PersonaMode,
    bool CommandMenuOwnerOnly,
    bool NonOwnerCommandsBlocked);

public sealed record AlifeVisionStatusResponse(
    bool Enabled,
    bool Ready,
    string Status,
    string Reason,
    string Provider,
    string Model,
    bool PublicUrlRequired,
    bool ApiKeyConfigured,
    int MaxImagesPerMessage);

public sealed record AlifeTtsStatusResponse(
    bool Enabled,
    bool Ready,
    string Status,
    string Reason,
    string Provider);
