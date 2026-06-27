using System.Collections.Generic;

namespace Alife.Function.Emotion;

public class EmotionLive2DParameterDriver
{
    public EmotionLive2DParameterDriver(
        PADEmotionEngine emotionEngine,
        IEmotionParameterSink parameterSink,
        EmotionParameterMapper? parameterMapper = null)
    {
        this.emotionEngine = emotionEngine;
        this.parameterSink = parameterSink;
        this.parameterMapper = parameterMapper ?? new EmotionParameterMapper();
    }

    public Dictionary<string, float> PushCurrentState()
    {
        Dictionary<string, float> parameters = parameterMapper.MapEmotionToParams(
            emotionEngine.Pleasure,
            emotionEngine.Arousal,
            emotionEngine.Dominance);
        parameterSink.SetParams(parameters);
        return parameters;
    }

    readonly PADEmotionEngine emotionEngine;
    readonly IEmotionParameterSink parameterSink;
    readonly EmotionParameterMapper parameterMapper;
}
