using System.Collections.Generic;

namespace Alife.Function.Emotion;

public interface IEmotionParameterSink
{
    void SetParams(Dictionary<string, float> parameters);
}
