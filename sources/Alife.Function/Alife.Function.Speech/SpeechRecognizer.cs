using System.Buffers;
using System.Runtime.InteropServices;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using SherpaOnnx;
using Alife.Basic;

namespace Alife.Function.Speech;

public class SpeechRecognizer : IDisposable
{
    public event Action<string>? Recognized;
    public bool IsRecognizing { get; private set; }
    public bool IsInitialized { get; private set; }

    public SpeechRecognizer()
    {
        // 下载语音识别模型
        const string SenseVoiceId = "pengzhendong/sherpa-onnx-sense-voice-zh-en-ja-ko-yue";
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
        const string VadId = "pengzhendong/silero-vad";
        string vadModelPath = AlifeModel.EnsureModelExisting(VadId, "silero_vad.onnx");
        VadModelConfig vadConfig = new();
        vadConfig.SileroVad.Model = vadModelPath;
        vadConfig.SileroVad.Threshold = 0.25f;
        vadConfig.SileroVad.MinSilenceDuration = 0.25f;
        vadConfig.SileroVad.MinSpeechDuration = 0.2f;
        vadConfig.SampleRate = 16000;
        vad = new VoiceActivityDetector(vadConfig, bufferSizeInSeconds: 30);
    }
    public void Dispose()
    {
        recognizer.Dispose();
        vad.Dispose();
        IsRecognizing = false;

        if (graph != null)
        {
            graph.QuantumStarted -= OnQuantumStarted;
            outputNode?.Dispose();
            inputNode?.Dispose();
            graph.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public async Task TryInitializeAudioAsync()
    {
        if (SpeechEnvironment.HasMicrophone() == false)
            return;
        if (IsInitialized)
            return;
        if (graph != null)//未初始化但有graph，说明是失效的
        {
            outputNode?.Dispose();
            outputNode = null;
            inputNode?.Dispose();
            inputNode = null;
            graph.Dispose();
            graph = null;
        }

        //创建语音专用 AudioGraph（支持回声消除）
        if (graph == null)
        {
            var settings = new AudioGraphSettings(AudioRenderCategory.Speech) {
                EncodingProperties = AudioEncodingProperties.CreatePcm(16000, 1, 32)
            };
            settings.EncodingProperties.Subtype = MediaEncodingSubtypes.Float;// 输出 32位 Float，Sherpa 和 Silero 直接可用
            var result = await AudioGraph.CreateAsync(settings);
            if (result.Status != AudioGraphCreationStatus.Success)
                throw new Exception($"AudioGraph 创建失败: {result.Status}");
            graph = result.Graph;

            //设置音频切片接收回调
            graph.QuantumStarted += OnQuantumStarted;
            //异常回调（重置状态）
            graph.UnrecoverableErrorOccurred += (sender, args) => {
                IsInitialized = false;
                IsRecognizing = false;
            };
        }

        if (outputNode == null)
            outputNode = graph.CreateFrameOutputNode(graph.EncodingProperties);

        //创建语音识别专用输入节点
        if (inputNode == null)
        {
            var inputResult = await graph.CreateDeviceInputNodeAsync(MediaCategory.Speech);
            if (inputResult.Status != AudioDeviceNodeCreationStatus.Success)
                throw new Exception($"AudioDeviceInputNode 创建失败: {inputResult.Status}");
            inputNode = inputResult.DeviceInputNode;
            inputNode.AddOutgoingConnection(outputNode);
        }

        IsInitialized = true;
    }
    public void Start()
    {
        if (IsRecognizing)
            throw new InvalidOperationException("已在运行中，Stop 后才能再次 Start。");
        if (IsInitialized == false)
            throw new Exception("音频录制系统未初始化！");

        graph?.Start();
        lock (vad)
            vad.Clear();
        IsRecognizing = true;
    }
    public void Stop()
    {
        if (IsRecognizing == false)
            throw new InvalidOperationException("未在运行中，Start 后才可调用 Stop。");

        graph?.Stop();
        IsRecognizing = false;
    }

    readonly OfflineRecognizer recognizer;
    readonly VoiceActivityDetector vad;
    AudioGraph? graph;
    AudioDeviceInputNode? inputNode;
    AudioFrameOutputNode? outputNode;

    unsafe void OnQuantumStarted(AudioGraph sender, object args)
    {
        if (outputNode == null)
        {
            Console.WriteLine("初始化异常，outputNode为空！");
            return;
        }

        using var frame = outputNode.GetFrame();
        using var buffer = frame.LockBuffer(Windows.Media.AudioBufferAccessMode.Read);
        using var reference = buffer.CreateReference();

        // C#/WinRT 的 IInspectable 在跨越本机 COM 边界时会遇到无法转换回原始接口的 bug
        // 这里通过 CsWinRT 暴露的 NativeObject 获取原生 IUnknown 指针，再通过 QueryInterface 和函数指针调用
        IntPtr unk = ((WinRT.IWinRTObject)reference).NativeObject.ThisPtr;
        Guid iid = new Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");// IMemoryBufferByteAccess
        if (Marshal.QueryInterface(unk, in iid, out IntPtr ptr) != 0)
        {
            Console.WriteLine("查询音频缓存区COM对象失败！");
            return;
        }

        try
        {
            // vtable 布局: IUnknown 有 3 个方法，GetBuffer 是第 4 个方法 (index 3)
            void** vtable = *(void***)ptr;
            var getBuffer = (delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, int>)vtable[3];

            byte* dataInBytes;
            uint capacityInBytes;
            getBuffer(ptr, &dataInBytes, &capacityInBytes);

            int sampleCount = (int)(capacityInBytes / sizeof(float));
            if (sampleCount > 0)
            {
                //保存音频切片到缓冲区
                float[] samples = new float[sampleCount];
                fixed (float* dest = samples)
                    Buffer.MemoryCopy(dataInBytes, dest, capacityInBytes, capacityInBytes);
                //由于是在后台线程，通过线程池投递处理，避免阻塞 AudioGraph
                ThreadPool.QueueUserWorkItem(_ => {
                    AcceptWaveform(samples);
                });
            }
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    void AcceptWaveform(float[] samples)
    {
        lock (vad)
        {
            if (IsRecognizing == false)
                return;//非识别期间不进行vad检测

            vad.AcceptWaveform(samples);
            while (vad.IsEmpty() == false)
            {
                //检测到可识别的声音
                if (IsRecognizing == false)
                {
                    vad.Reset();
                    return;//非识别期间取消识别
                }

                SpeechSegment segment = vad.Front();
                if (segment.Samples is { Length: > 0 })
                    ProcessSegment(segment.Samples);
                vad.Pop();
            }
        }
    }

    void ProcessSegment(float[] samples)
    {
        using OfflineStream stream = recognizer.CreateStream();
        stream.AcceptWaveform(16000, samples);
        recognizer.Decode(stream);

        if (string.IsNullOrWhiteSpace(stream.Result.Text) == false)
            Recognized?.Invoke(stream.Result.Text);
    }
}
