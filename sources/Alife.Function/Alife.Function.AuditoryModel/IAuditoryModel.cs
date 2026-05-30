using System;

namespace Alife.Function.Speech;

public interface IAuditoryModel
{
    event Action<string>? Recognized;
    void AcceptWaveform(float[] samples);
}
