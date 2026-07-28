namespace Alife.Function.Speech;

public sealed class MiMoDesignSpeechModelConfig
{
    public string ApiEndpoint { get; set; } = "https://api.xiaomimimo.com/v1/chat/completions";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "mimo-v2.5-tts-voicedesign";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxTextChars { get; set; } = 120;
    public string DefaultVoiceDesignDescription { get; set; } = "";
    public GptSoVitsSpeechModelConfig LocalFallback { get; set; } = new();
}
