using System;
using System.IO;
using SherpaOnnx;
using Alife.Framework;
using Alife.Platform;

namespace Alife.Function.Speech;

[Plugin("SenseVoice语音识别", "基于SenseVoice的本地语音识别引擎",
defaultCategory: "Alife 官方/模型接入/听觉模型",
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
        lock (vad)
        {
            vad.AcceptWaveform(samples);
            while (vad.IsEmpty() == false)
            {
                SpeechSegment segment = vad.Front();
                if (segment.Samples is { Length: > 0 })
                    ProcessSegment(segment.Samples);
                vad.Pop();
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

        if (string.IsNullOrWhiteSpace(stream.Result.Text) == false)
            Recognized?.Invoke(stream.Result.Text);
    }

    public SenseVoiceAuditoryModel()
    {
        // 下载语音识别模型
        string senseVoicePath = AlifeModel.EnsureModelExisting(SenseVoiceId);
        OfflineRecognizerConfig config = new();
        config.ModelConfig.SenseVoice.Model = Path.Combine(senseVoicePath, "model.int8.onnx");
        config.ModelConfig.SenseVoice.Language = "zh";
        config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
        config.ModelConfig.Tokens = Path.Combine(senseVoicePath, "tokens.txt");
        config.ModelConfig.NumThreads = 1;
        config.ModelConfig.Debug = 0;
        recognizer = new OfflineRecognizer(config);

        // 下载语音检测模型
        string vadModelPath = AlifeModel.EnsureModelExisting(VadId, "silero_vad.onnx");
        VadModelConfig vadConfig = new();
        vadConfig.SileroVad.Model = vadModelPath;
        vadConfig.SileroVad.Threshold = 0.4f;
        vadConfig.SileroVad.MinSilenceDuration = 0.3f;
        vadConfig.SileroVad.MinSpeechDuration = 0.25f;
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
