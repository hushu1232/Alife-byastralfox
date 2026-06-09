using System;
using System.Collections.Generic;

namespace Alife.Function.Emotion;

public class EmotionParameterMapper
{
    public float HeadAngleScale { get; set; } = 10f;
    public float BodyAngleScale { get; set; } = 5f;
    public float EyeOpenScale { get; set; } = 0.3f;
    public float MouthOpenScale { get; set; } = 0.2f;
    public float BrowScale { get; set; } = 0.3f;

    public Dictionary<string, float> MapEmotionToParams(float pleasure, float arousal, float dominance)
    {
        return new Dictionary<string, float>
        {
            ["ParamAngleX"] = pleasure * HeadAngleScale,
            ["ParamAngleY"] = arousal * HeadAngleScale * 0.5f,
            ["ParamAngleZ"] = pleasure * HeadAngleScale * 0.5f,
            ["ParamBodyAngleX"] = pleasure * BodyAngleScale,
            ["ParamBodyAngleY"] = arousal * BodyAngleScale * 0.3f,
            ["ParamBodyAngleZ"] = pleasure * BodyAngleScale * 0.3f,
            ["ParamEyeLOpen"] = Math.Clamp(1f - arousal * EyeOpenScale, 0.2f, 1f),
            ["ParamEyeROpen"] = Math.Clamp(1f - arousal * EyeOpenScale, 0.2f, 1f),
            ["ParamMouthOpenY"] = Math.Clamp(arousal * MouthOpenScale, 0f, 0.5f),
            ["ParamBrowLY"] = pleasure * BrowScale,
            ["ParamBrowRY"] = pleasure * BrowScale
        };
    }

    public string? MapEmotionToExpression(PetEmotion emotion)
    {
        return emotion switch
        {
            PetEmotion.Happy => "happy",
            PetEmotion.Sad => "sad",
            PetEmotion.Shy => "shy",
            PetEmotion.Angry => "angry",
            _ => null
        };
    }
}
