using System;
using System.Threading.Tasks;
using Alife.Framework;

namespace Alife.Function.Emotion;

[Module("PAD情绪引擎", "基于愉悦度/唤醒度/支配度的三维情绪模型", defaultCategory: "Alife 官方/智能体")]
public class PADEmotionEngine : InteractiveModule<PADEmotionEngine>, IConfigurable<EmotionConfig>, ITimeIterative
{
    public EmotionConfig? Configuration
    {
        get => configuration;
        set
        {
            configuration = value ?? new EmotionConfig();
            ResetState();
        }
    }

    public float RawPleasure => rawPleasure;
    public float RawArousal => rawArousal;
    public float RawDominance => rawDominance;
    public float Pleasure => smoothPleasure;
    public float Arousal => smoothArousal;
    public float Dominance => smoothDominance;
    public PetEmotion CurrentEmotion => PADToEmotion(smoothPleasure, smoothArousal, smoothDominance);
    public string EmotionLabel => PADToLabel(smoothPleasure, smoothArousal, smoothDominance);

    public PADEmotionEngine()
    {
        configuration = new EmotionConfig();
        ResetState();
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);
        Prompt(GetEmotionPromptContext());
    }

    public void OnUpdate(ref float time)
    {
        float deltaSeconds = Math.Max(0f, time);
        ApplyDecay(deltaSeconds);
        UpdateSmoothedValues(deltaSeconds);
    }

    public void ApplyEvent(EmotionEvent emotionEvent)
    {
        rawPleasure = ClampPad(rawPleasure + emotionEvent.PleasureDelta);
        rawArousal = ClampPad(rawArousal + emotionEvent.ArousalDelta);
        rawDominance = ClampPad(rawDominance + emotionEvent.DominanceDelta);

        if (emotionEvent.Duration > 0f)
        {
            decayMultiplierPleasure = 0.1f;
            decayMultiplierArousal = 0.1f;
        }
    }

    public void ApplyEventType(EmotionEventType type)
    {
        ApplyEvent(GetPredefinedEvent(type));
    }

    public void ModulatePAD(float pleasureDelta, float arousalDelta, float dominanceDelta)
    {
        rawPleasure = ClampPad(rawPleasure + pleasureDelta);
        rawArousal = ClampPad(rawArousal + arousalDelta);
        rawDominance = ClampPad(rawDominance + dominanceDelta);
    }

    public string GetEmotionPromptContext()
    {
        string mood = smoothPleasure switch
        {
            > 0.5f => "很开心",
            > 0.2f => "心情不错",
            > -0.2f => "心情平静",
            > -0.5f => "有点不开心",
            _ => "很难过"
        };

        string energy = smoothArousal switch
        {
            > 0.5f => "精力充沛",
            > 0.2f => "比较精神",
            > -0.2f => "状态一般",
            > -0.5f => "有点困",
            _ => "很困倦"
        };

        string confidence = smoothDominance switch
        {
            > 0.5f => "很自信",
            > 0.2f => "比较大方",
            > -0.2f => "态度平常",
            > -0.5f => "有点害羞",
            _ => "很胆怯"
        };

        return $"当前情绪: 心情{mood}, 精力{energy}, 自信{confidence}。";
    }

    public static EmotionEvent GetPredefinedEvent(EmotionEventType type)
    {
        return type switch
        {
            EmotionEventType.Petted => new EmotionEvent(type, +0.3f, +0.2f, +0.1f),
            EmotionEventType.Ignored => new EmotionEvent(type, -0.1f, -0.15f, -0.05f),
            EmotionEventType.PositiveChat => new EmotionEvent(type, +0.2f, +0.15f, +0.1f),
            EmotionEventType.NegativeChat => new EmotionEvent(type, -0.2f, +0.05f, +0.1f),
            EmotionEventType.WakeUp => new EmotionEvent(type, +0.05f, +0.5f, +0.05f),
            EmotionEventType.FallAsleep => new EmotionEvent(type, 0f, -0.4f, -0.1f),
            EmotionEventType.Dragged => new EmotionEvent(type, -0.1f, +0.3f, -0.2f),
            EmotionEventType.Fed => new EmotionEvent(type, +0.35f, +0.25f, +0.05f),
            EmotionEventType.Scared => new EmotionEvent(type, -0.4f, +0.5f, -0.4f),
            EmotionEventType.Complimented => new EmotionEvent(type, +0.4f, +0.1f, +0.2f),
            EmotionEventType.Insulted => new EmotionEvent(type, -0.3f, +0.2f, +0.15f),
            EmotionEventType.PlayedWith => new EmotionEvent(type, +0.3f, +0.3f, +0.15f),
            EmotionEventType.LongAbsence => new EmotionEvent(type, -0.25f, -0.3f, -0.15f),
            _ => new EmotionEvent(type, 0f, 0f, 0f)
        };
    }

    public static PetEmotion PADToEmotion(float pleasure, float arousal, float dominance)
    {
        float happyDistance = PADDistance(pleasure, arousal, dominance, +0.7f, +0.5f, +0.4f);
        float sadDistance = PADDistance(pleasure, arousal, dominance, -0.7f, -0.4f, -0.5f);
        float shyDistance = PADDistance(pleasure, arousal, dominance, +0.1f, -0.3f, -0.5f);
        float angryDistance = PADDistance(pleasure, arousal, dominance, -0.6f, +0.5f, +0.5f);
        float neutralDistance = PADDistance(pleasure, arousal, dominance, 0f, 0f, 0f) * 1.2f;
        float minimumDistance = Math.Min(Math.Min(Math.Min(happyDistance, sadDistance), Math.Min(shyDistance, angryDistance)), neutralDistance);

        if (minimumDistance >= happyDistance) return PetEmotion.Happy;
        if (minimumDistance >= sadDistance) return PetEmotion.Sad;
        if (minimumDistance >= shyDistance) return PetEmotion.Shy;
        if (minimumDistance >= angryDistance) return PetEmotion.Angry;
        return PetEmotion.Neutral;
    }

    EmotionConfig configuration;
    float rawPleasure;
    float rawArousal;
    float rawDominance;
    float smoothPleasure;
    float smoothArousal;
    float smoothDominance;
    float velocityPleasure;
    float velocityArousal;
    float velocityDominance;
    float decayMultiplierPleasure = 1f;
    float decayMultiplierArousal = 1f;
    float decayMultiplierDominance = 1f;

    void ResetState()
    {
        rawPleasure = ClampPad(configuration.InitialPleasure);
        rawArousal = ClampPad(configuration.InitialArousal);
        rawDominance = ClampPad(configuration.InitialDominance);
        smoothPleasure = rawPleasure;
        smoothArousal = rawArousal;
        smoothDominance = rawDominance;
        velocityPleasure = 0f;
        velocityArousal = 0f;
        velocityDominance = 0f;
    }

    void ApplyDecay(float deltaSeconds)
    {
        rawPleasure = MoveToward(rawPleasure, configuration.PleasureBaseline, configuration.PleasureDecayRate * decayMultiplierPleasure * deltaSeconds);
        rawArousal = MoveToward(rawArousal, configuration.ArousalBaseline, configuration.ArousalDecayRate * decayMultiplierArousal * deltaSeconds);
        rawDominance = MoveToward(rawDominance, configuration.DominanceBaseline, configuration.DominanceDecayRate * decayMultiplierDominance * deltaSeconds);
        decayMultiplierPleasure = 1f;
        decayMultiplierArousal = 1f;
        decayMultiplierDominance = 1f;
    }

    void UpdateSmoothedValues(float deltaSeconds)
    {
        smoothPleasure = SmoothDamp(smoothPleasure, rawPleasure, ref velocityPleasure, configuration.SmoothTime, deltaSeconds);
        smoothArousal = SmoothDamp(smoothArousal, rawArousal, ref velocityArousal, configuration.SmoothTime, deltaSeconds);
        smoothDominance = SmoothDamp(smoothDominance, rawDominance, ref velocityDominance, configuration.SmoothTime, deltaSeconds);
    }

    static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float deltaSeconds)
    {
        float effectiveSmoothTime = Math.Max(0.0001f, smoothTime);
        float omega = 2f / effectiveSmoothTime;
        float x = omega * deltaSeconds;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float change = current - target;
        float temp = (velocity + omega * change) * deltaSeconds;
        velocity = (velocity - omega * temp) * exp;
        return target + (change + temp) * exp;
    }

    static float MoveToward(float current, float target, float maxDelta)
    {
        if (current < target)
            return Math.Min(current + maxDelta, target);

        return Math.Max(current - maxDelta, target);
    }

    static float ClampPad(float value)
    {
        return Math.Clamp(value, -1f, 1f);
    }

    static float PADDistance(float pleasure, float arousal, float dominance, float centerPleasure, float centerArousal, float centerDominance)
    {
        float pleasureDelta = pleasure - centerPleasure;
        float arousalDelta = arousal - centerArousal;
        float dominanceDelta = dominance - centerDominance;
        return pleasureDelta * pleasureDelta + arousalDelta * arousalDelta + dominanceDelta * dominanceDelta;
    }

    static string PADToLabel(float pleasure, float arousal, float dominance)
    {
        return PADToEmotion(pleasure, arousal, dominance) switch
        {
            PetEmotion.Happy => "开心",
            PetEmotion.Sad => "难过",
            PetEmotion.Shy => "害羞",
            PetEmotion.Angry => "生气",
            _ => "平静"
        };
    }
}
