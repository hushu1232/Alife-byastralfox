using System;

namespace Alife.Function.WebBridge;

public sealed class AlifeManagementApiService
{
    public AlifeManagementApiService(
        string agent,
        string ownerId,
        string botId,
        bool qchatEnabled,
        bool visionEnabled,
        bool ttsEnabled,
        bool outboxEnabled,
        string personaMode = "default",
        string visionStatus = "ready",
        string visionReason = "ready",
        string visionModel = "agnes-2.0-flash",
        int visionMaxImagesPerMessage = 4,
        string ttsStatus = "ready",
        string ttsReason = "ready")
    {
        this.agent = agent;
        this.ownerId = ownerId;
        this.botId = botId;
        this.qchatEnabled = qchatEnabled;
        this.visionEnabled = visionEnabled;
        this.ttsEnabled = ttsEnabled;
        this.outboxEnabled = outboxEnabled;
        this.personaMode = personaMode;
        this.visionStatus = Normalize(visionStatus, visionEnabled ? "ready" : "disabled");
        this.visionReason = Normalize(visionReason, visionEnabled ? "ready" : "vision_disabled");
        this.visionModel = Normalize(visionModel, "agnes-2.0-flash");
        this.visionMaxImagesPerMessage = Math.Max(1, visionMaxImagesPerMessage);
        this.ttsStatus = Normalize(ttsStatus, ttsEnabled ? "ready" : "disabled");
        this.ttsReason = Normalize(ttsReason, ttsEnabled ? "ready" : "tts_disabled");
    }

    public AlifeHealthResponse GetHealth()
    {
        return new AlifeHealthResponse("healthy", "Alife", "local", DateTimeOffset.UtcNow);
    }

    public AlifeRuntimeStatusResponse GetStatus()
    {
        return new AlifeRuntimeStatusResponse(
            Status: "healthy",
            Agent: agent,
            OwnerId: ownerId,
            BotId: botId,
            QChatEnabled: qchatEnabled,
            VisionEnabled: visionEnabled,
            VisionStatus: visionEnabled ? visionStatus : "disabled",
            VisionReason: visionEnabled ? visionReason : "vision_disabled",
            TtsEnabled: ttsEnabled,
            TtsStatus: ttsEnabled ? ttsStatus : "disabled",
            TtsReason: ttsEnabled ? ttsReason : "tts_disabled",
            OutboxEnabled: outboxEnabled,
            TimestampUtc: DateTimeOffset.UtcNow);
    }

    public AlifeQChatStatusResponse GetQChatStatus()
    {
        return new AlifeQChatStatusResponse(
            Enabled: qchatEnabled,
            Agent: agent,
            OwnerId: ownerId,
            BotId: botId,
            PersonaMode: personaMode,
            CommandMenuOwnerOnly: true,
            NonOwnerCommandsBlocked: true);
    }

    public AlifeVisionStatusResponse GetVisionStatus(bool apiKeyConfigured)
    {
        return new AlifeVisionStatusResponse(
            Enabled: visionEnabled,
            Ready: visionEnabled && apiKeyConfigured && string.Equals(visionStatus, "ready", StringComparison.OrdinalIgnoreCase),
            Status: visionEnabled ? visionStatus : "disabled",
            Reason: visionEnabled ? visionReason : "vision_disabled",
            Provider: "agnes",
            Model: visionModel,
            PublicUrlRequired: true,
            ApiKeyConfigured: apiKeyConfigured,
            MaxImagesPerMessage: visionMaxImagesPerMessage);
    }

    public AlifeTtsStatusResponse GetTtsStatus()
    {
        return new AlifeTtsStatusResponse(
            Enabled: ttsEnabled,
            Ready: ttsEnabled && string.Equals(ttsStatus, "ready", StringComparison.OrdinalIgnoreCase),
            Status: ttsEnabled ? ttsStatus : "disabled",
            Reason: ttsEnabled ? ttsReason : "tts_disabled",
            Provider: "gpt-sovits");
    }

    static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    readonly string agent;
    readonly string ownerId;
    readonly string botId;
    readonly bool qchatEnabled;
    readonly bool visionEnabled;
    readonly bool ttsEnabled;
    readonly bool outboxEnabled;
    readonly string personaMode;
    readonly string visionStatus;
    readonly string visionReason;
    readonly string visionModel;
    readonly int visionMaxImagesPerMessage;
    readonly string ttsStatus;
    readonly string ttsReason;
}
