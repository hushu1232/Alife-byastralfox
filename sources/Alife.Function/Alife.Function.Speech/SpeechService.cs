using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Alife.Platform;
using Alife.Framework;
using Alife.Function.Interpreter;
using Alife.Function.Speech;
using Microsoft.SemanticKernel;

namespace Alife.Function.Speech;

public enum SpeechSynthesizerType
{
    Edge,
    Vits,
    Genie
}

public class SpeechConfig
{
    public bool EnabledRecognition { get; set; } = true;
    public SpeechSynthesizerType SynthesizerType { get; set; } = SpeechSynthesizerType.Edge;
    public string EdgeVoiceTone { get; set; } = "zh-CN-XiaoyiNeural";
    public int VitsSpeakerId { get; set; } = 551;
    public float VitsNoiseScale { get; set; } = 0.6f;
    public float VitsNoiseScaleW { get; set; } = 0.668f;
    public float VitsLengthScale { get; set; } = 1.2f;
}

public partial class SpeechService
{
    public static SpeechRecognizer? Recognizer { get; private set; }
    public static bool IsRecognizing => Recognizer is { IsRunning: true };

    static void TryInitializedAsync()
    {
        Recognizer ??= new SpeechRecognizer();
    }
}

[Plugin("语音对话", "为AI增加语音识别（基于本地模型）和语音转文字输出（基于edge-tts/VITS）的能力。",
defaultCategory: "Alife 官方/交互方式",
EditorUI = typeof(SpeechServiceUI))]
[Description("此服务让你获得能将文字以语音形式输出的能力。")]
public partial class SpeechService(XmlFunctionCaller functionService)
    : InteractivePlugin<SpeechService>, IAsyncDisposable, IConfigurable<SpeechConfig>
{
    public SpeechConfig? Configuration
    {
        get => configuration;
        set
        {
            configuration = value;
            if (configuration != null && synthesizer != null)
            {
                if (synthesizer is EdgeSpeechSynthesizer edge)
                {
                    edge.VoiceTone = configuration.EdgeVoiceTone;
                }
                else if (synthesizer is VitsSpeechSynthesizer vits)
                {
                    vits.SpeakerId = configuration.VitsSpeakerId;
                    vits.NoiseScale = configuration.VitsNoiseScale;
                    vits.NoiseScaleW = configuration.VitsNoiseScaleW;
                    vits.LengthScale = configuration.VitsLengthScale;
                }
            }
        }
    }
    public SpeechSynthesizer? Synthesizer => synthesizer;
    public bool IsSynthesizing => synthesizer?.IsSpeaking ?? false;

    [XmlFunction(FunctionMode.Content, order: -10)]
    [Description("将文本以语音方式输出。")]
    public async Task Speak(XmlExecutorContext context, CancellationToken cancellationToken)
    {
        try
        {
            switch (context.CallMode)
            {
                case CallMode.Opening:
                    try
                    {
                        if (synthesizer is { IsSpeaking: true })
                            await synthesizer.LastSpeaking;
                    }
                    catch (OperationCanceledException) {}
                    break;
                case CallMode.Closing:
                    break;
                case CallMode.Content:
                {
                    string content = context.Content.Trim();
                    if (string.IsNullOrWhiteSpace(content))
                        break;
                    if (synthesizer != null)
                        await synthesizer.SpeakAsync(content, cancellationToken);
                    break;
                }
            }
        }
        catch (OperationCanceledException) {}
    }

    protected override string ChatPrefixPrompt => "[语音识别的信息，请用Speak回复]";
    SpeechSynthesizer? synthesizer;
    SpeechConfig? configuration;

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        TryInitializedAsync();// 语音识别
        InitializeSynthesizer();// 语音合成

        functionService.RegisterHandler(this);

        void InitializeSynthesizer()
        {
            if (configuration == null)
                return;

            try
            {
                if (configuration.SynthesizerType == SpeechSynthesizerType.Vits)
                {
                    synthesizer = new VitsSpeechSynthesizer(
                    noiseScale: configuration.VitsNoiseScale,
                    noiseScaleW: configuration.VitsNoiseScaleW,
                    lengthScale: configuration.VitsLengthScale,
                    speakerId: configuration.VitsSpeakerId
                    );
                }
                else if (configuration.SynthesizerType == SpeechSynthesizerType.Genie)
                {
                    synthesizer = new GenieSpeechSynthesizer();
                }
                else
                {
                    synthesizer = new EdgeSpeechSynthesizer(configuration.EdgeVoiceTone);
                }
            }
            catch (Exception ex)
            {
                AlifeTerminal.LogWarning($"Failed to initialize speech synthesizer ({configuration.SynthesizerType}): {ex.Message}");
            }
        }
    }
    public override async Task StartAsync(Kernel kernel, ChatActivity chatActivity)
    {
        await base.StartAsync(kernel, chatActivity);

        if (Recognizer == null)
            throw new ArgumentNullException(nameof(Recognizer));
        await Recognizer.TryStartAsync();
        Recognizer.Recognized += OnRecognized;// 开始接收语音识别
    }
    public override async Task DestroyAsync()
    {
        if (Recognizer != null)
            Recognizer.Recognized -= OnRecognized;
        await base.DestroyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (synthesizer != null)
        {
            try
            {
                if (synthesizer.IsSpeaking)
                    await synthesizer.LastSpeaking;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            synthesizer.Dispose();
        }
    }

    void OnRecognized(string text)
    {
        if (configuration!.EnabledRecognition)
            Chat(text);
    }
}
