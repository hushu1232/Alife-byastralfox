using System;
using System.IO;
using SherpaOnnx;
using Alife.Framework;
using Alife.Platform;

namespace Alife.Function.Speech;

[Module("SenseVoice语音识别", "基于SenseVoice的本地语音识别引擎",
    defaultCategory: "astralfox-alife/模型接入/听觉模型",
    EditorUI = typeof(SenseVoiceAuditoryModelUI))]
public class SenseVoiceAuditoryModel :
    IAuditoryModel,
    IDisposable,
    IConfigurable<SenseVoiceAuditoryModelConfig>
{
    public static bool ModelsExists
    {
        get
        {
            string senseVoicePath = Path.Combine(AlifeModel.ModelScopeModelPath, SenseVoiceId.Replace(".", "___"));
            string vadPath = Path.Combine(AlifeModel.ModelScopeModelPath, VadId.Replace(".", "___"));
            return File.Exists(Path.Combine(senseVoicePath, "model.int8.onnx"))
                   && File.Exists(Path.Combine(vadPath, "silero_vad.onnx"));
        }
    }

    public SenseVoiceAuditoryModelConfig? Configuration { get; set; }

    public event Action<string>? Recognized;
    public void AcceptWaveform(float[] samples)
    {
        var detector = vad;
        lock (detector)
        {
            detector.AcceptWaveform(samples);
            while (detector.IsEmpty() == false)
            {
                SpeechSegment segment = detector.Front();
                if (segment.Samples is { Length: > 0 })
                    ProcessSegment(segment.Samples);
                detector.Pop();
            }
        }
    }

    const string SenseVoiceId = "pengzhendong/sherpa-onnx-sense-voice-zh-en-ja-ko-yue";
    const string VadId = "pengzhendong/silero-vad";
    readonly OfflineRecognizer recognizer;
    readonly VoiceActivityDetector vad;

    void ProcessSegment(float[] samples)
    {
        using OfflineStream stream = recognizer.CreateStream();
        stream.AcceptWaveform(16000, samples);
        recognizer.Decode(stream);

        string text = stream.Result.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (text == "。")
            return;
        Recognized?.Invoke(text);
    }

    public SenseVoiceAuditoryModel()
    {
        string senseVoicePath = AlifeModel.EnsureModelExisting(SenseVoiceId);
        string vadModelPath = AlifeModel.EnsureModelExisting(VadId, "silero_vad.onnx");

        OfflineRecognizerConfig config = new();
        config.ModelConfig.SenseVoice.Model = Path.Combine(senseVoicePath, "model.int8.onnx");
        config.ModelConfig.SenseVoice.Language = Configuration?.Language ?? "zh";
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = (Configuration?.UseInverseTextNormalization ?? true) ? 1 : 0;
        config.ModelConfig.Tokens = Path.Combine(senseVoicePath, "tokens.txt");
        config.ModelConfig.NumThreads = Configuration?.NumThreads ?? 1;
        config.ModelConfig.Debug = 0;
        recognizer = new OfflineRecognizer(config);

        VadModelConfig vadConfig = new();
        vadConfig.SileroVad.Model = vadModelPath;
        vadConfig.SileroVad.Threshold = Configuration?.VadThreshold ?? 0.5f;
        vadConfig.SileroVad.MinSilenceDuration = Configuration?.VadMinSilenceDuration ?? 0.5f;
        vadConfig.SileroVad.MinSpeechDuration = Configuration?.VadMinSpeechDuration ?? 0.25f;
        vadConfig.SampleRate = 16000;
        vad = new VoiceActivityDetector(vadConfig, bufferSizeInSeconds: 30);
    }
    public void Dispose()
    {
        recognizer.Dispose();
        vad.Dispose();
        GC.SuppressFinalize(this);
    }
}
