namespace Alife.Function.WebBridge;

public sealed class AlifeManagementApiOptions
{
    public bool Enabled { get; set; }
    public string BindUrl { get; set; } = "http://127.0.0.1:8787/";
    public bool RequireBearerToken { get; set; } = true;
    public string BearerTokenEnvironmentVariable { get; set; } = "ALIFE_WEB_MANAGEMENT_TOKEN";
    public int RequestTimeoutSeconds { get; set; } = 10;
}

public sealed class AlifeManagementStatusOptions
{
    public string Agent { get; set; } = "local";
    public string OwnerId { get; set; } = "";
    public string BotId { get; set; } = "";
    public bool QChatEnabled { get; set; }
    public bool VisionEnabled { get; set; }
    public string VisionStatus { get; set; } = "disabled";
    public string VisionReason { get; set; } = "vision_disabled";
    public string VisionModel { get; set; } = "agnes-2.0-flash";
    public int VisionMaxImagesPerMessage { get; set; } = 4;
    public bool TtsEnabled { get; set; }
    public string TtsStatus { get; set; } = "disabled";
    public string TtsReason { get; set; } = "tts_disabled";
    public bool OutboxEnabled { get; set; }
    public string PersonaMode { get; set; } = "default";
}
