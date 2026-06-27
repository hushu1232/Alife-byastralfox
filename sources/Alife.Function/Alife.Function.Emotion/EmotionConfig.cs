namespace Alife.Function.Emotion;

public class EmotionConfig
{
    public float InitialPleasure { get; set; } = 0.2f;
    public float InitialArousal { get; set; } = 0.3f;
    public float InitialDominance { get; set; } = 0.1f;
    public float PleasureDecayRate { get; set; } = 0.003f;
    public float ArousalDecayRate { get; set; } = 0.005f;
    public float DominanceDecayRate { get; set; } = 0.0005f;
    public float PleasureBaseline { get; set; }
    public float ArousalBaseline { get; set; }
    public float DominanceBaseline { get; set; } = 0.1f;
    public float SmoothTime { get; set; } = 0.15f;
}
