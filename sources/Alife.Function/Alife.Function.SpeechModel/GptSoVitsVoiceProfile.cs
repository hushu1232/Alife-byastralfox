namespace Alife.Function.Speech;

public sealed record GptSoVitsVoiceProfile
{
    public string VoiceId { get; init; } = "";
    public string AgentId { get; init; } = "";
    public long BotId { get; init; }
    public string ApiBaseUrl { get; init; } = "";
    public string VoiceRootPath { get; init; } = "";
    public string ReferenceAudioPath { get; init; } = "";
    public string GptWeightsPath { get; init; } = "";
    public string SovitsWeightsPath { get; init; } = "";
    public string PromptText { get; init; } = "";
    public string TextLanguage { get; init; } = "";
    public string PromptLanguage { get; init; } = "";
    public int? MaxTextChars { get; init; }
}
