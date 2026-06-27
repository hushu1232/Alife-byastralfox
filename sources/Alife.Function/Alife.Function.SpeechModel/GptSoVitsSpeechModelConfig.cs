namespace Alife.Function.Speech;

public class GptSoVitsSpeechModelConfig
{
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:9880";
    public string VoiceId { get; set; } = "xiayu";
    public string VoiceRootPath { get; set; } = "";
    public string ReferenceAudioPath { get; set; } = "";
    public string GptWeightsPath { get; set; } = "";
    public string SovitsWeightsPath { get; set; } = "";
    public string PromptText { get; set; } = "";
    public string TextLanguage { get; set; } = "zh";
    public string PromptLanguage { get; set; } = "zh";
    public string TextSplitMethod { get; set; } = "cut5";
    public string MediaType { get; set; } = "wav";
    public int MaxTextChars { get; set; } = 120;
    public int TimeoutSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 1;
    public float SpeedFactor { get; set; } = 1.0f;
    public int TopK { get; set; } = 15;
    public float TopP { get; set; } = 1.0f;
    public float Temperature { get; set; } = 1.0f;
    public bool ParallelInfer { get; set; } = false;
    public float RepetitionPenalty { get; set; } = 1.35f;
    public bool EnableCache { get; set; } = true;
    public bool AllowPersonaFallbackToEdgeTts { get; set; } = false;
}
