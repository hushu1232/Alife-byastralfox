using Alife.Function.Speech;

namespace Alife.Test.Speech;

[TestFixture]
public sealed class GptSoVitsManualSmokeTests
{
    static readonly string KayokoRoot = Path.Combine(
        "D:\\lobotomy",
        "GPT\u6a21\u578b",
        "\u30d6\u30eb\u30fc\u30a2\u30fc\u30ab\u30a4\u30d6",
        "\u4f73\u4ee3\u5b50");

    static IEnumerable<TestCaseData> Samples()
    {
        yield return new TestCaseData(new ListeningSample(
            VoiceId: "xiayu-zh-kayoko",
            TextLanguage: "zh",
            PromptLanguage: "zh",
            ReferenceRelativePath: Path.Combine(
                "\u4e2d",
                "\u97f3\u9891\u6587\u4ef6",
                "\u4e3b",
                "768710.ogg_0000000000_0000162560.wav"),
            GptWeightsRelativePath: Path.Combine(
                "\u4e2d",
                "GPT_weights_v2",
                "Kayoko-Zh-e50.ckpt"),
            SovitsWeightsRelativePath: Path.Combine(
                "\u4e2d",
                "SoVITS_weights_v2",
                "Kayoko-Zh_e8_s664.pth"),
            PromptText: "\u5723\u8bde\u5feb\u4e50\uff0c\u8fd9\u662f\u60c5\u4fa3\u4eec\u7684\u8282\u65e5\u554a\u3002",
            SampleText: "\u8001\u5e08\uff0c\u4eca\u5929\u4e5f\u8bf7\u591a\u6307\u6559\u3002",
            OutputFileName: "xiayu-zh-kayoko-listening.wav"))
            .SetName("XiayuKayokoProfile_GeneratesChineseListeningSample");

        yield return new TestCaseData(new ListeningSample(
            VoiceId: "xiayu-ja-kayoko",
            TextLanguage: "ja",
            PromptLanguage: "ja",
            ReferenceRelativePath: Path.Combine(
                "\u65e5",
                "\u97f3\u9891\u6587\u4ef6",
                "\u4e3b",
                "76078.ogg_0000664320_0000819520.wav"),
            GptWeightsRelativePath: Path.Combine(
                "\u65e5",
                "GPT_weights_v2",
                "Kayoko-ja-e50.ckpt"),
            SovitsWeightsRelativePath: Path.Combine(
                "\u65e5",
                "SoVITS_weights_v2",
                "Kayoko-Ja_e8_s400.pth"),
            PromptText: "\u3042\u308a\u304c\u3068\u3046\u3001\u5148\u751f\u3002\u3053\u308c\u304b\u3089\u3082",
            SampleText: "\u5148\u751f\u3001\u4eca\u65e5\u3082\u3088\u308d\u3057\u304f\u304a\u9858\u3044\u3057\u307e\u3059\u3002",
            OutputFileName: "xiayu-ja-kayoko-listening.wav"))
            .SetName("XiayuKayokoProfile_GeneratesJapaneseListeningSample");
    }

    [TestCaseSource(nameof(Samples))]
    [Explicit("Manual GPT-SoVITS listening smoke test. Requires a local GPT-SoVITS API at http://127.0.0.1:9880/tts.")]
    public async Task XiayuKayokoProfile_GeneratesListeningSample(ListeningSample sample)
    {
        string referenceAudioPath = Path.Combine(KayokoRoot, sample.ReferenceRelativePath);
        string gptWeightsPath = Path.Combine(KayokoRoot, sample.GptWeightsRelativePath);
        string sovitsWeightsPath = Path.Combine(KayokoRoot, sample.SovitsWeightsRelativePath);

        Assert.That(File.Exists(referenceAudioPath), Is.True, $"Reference audio missing: {referenceAudioPath}");
        Assert.That(File.Exists(gptWeightsPath), Is.True, $"GPT weights missing: {gptWeightsPath}");
        Assert.That(File.Exists(sovitsWeightsPath), Is.True, $"SoVITS weights missing: {sovitsWeightsPath}");

        using var controlClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        await SetWeightsAsync(controlClient, "set_gpt_weights", "weights_path", gptWeightsPath);
        await SetWeightsAsync(controlClient, "set_sovits_weights", "weights_path", sovitsWeightsPath);

        using var model = new GptSoVitsSpeechModel
        {
            Configuration = new GptSoVitsSpeechModelConfig
            {
                ApiBaseUrl = "http://127.0.0.1:9880",
                EnableCache = false,
                TimeoutSeconds = 120
            }
        };

        string? generatedPath = await model.GenerateSpeechFileAsync(sample.SampleText, new GptSoVitsVoiceProfile
        {
            VoiceId = sample.VoiceId,
            AgentId = "xiayu",
            BotId = 2905391496,
            ReferenceAudioPath = referenceAudioPath,
            PromptText = sample.PromptText,
            TextLanguage = sample.TextLanguage,
            PromptLanguage = sample.PromptLanguage,
            MaxTextChars = 120
        });

        Assert.That(generatedPath, Is.Not.Null, "GPT-SoVITS did not return a usable wav file. Confirm the local API is running and has the intended weights loaded.");

        string listeningDirectory = Path.Combine("D:\\Alife", "Temp", "GPT-SoVITS", "voice-smoke");
        Directory.CreateDirectory(listeningDirectory);

        string listeningPath = Path.Combine(listeningDirectory, sample.OutputFileName);
        File.Copy(generatedPath!, listeningPath, overwrite: true);

        TestContext.Out.WriteLine($"Generated {sample.VoiceId} sample: {listeningPath}");
        Assert.That(new FileInfo(listeningPath).Length, Is.GreaterThan(0));
    }

    static async Task SetWeightsAsync(
        HttpClient client,
        string endpoint,
        string parameterName,
        string weightsPath)
    {
        string requestUri = $"http://127.0.0.1:9880/{endpoint}?{parameterName}={Uri.EscapeDataString(weightsPath)}";
        using HttpResponseMessage response = await client.GetAsync(requestUri);
        string body = await response.Content.ReadAsStringAsync();

        Assert.That(
            response.IsSuccessStatusCode,
            Is.True,
            $"GPT-SoVITS {endpoint} failed with HTTP {(int)response.StatusCode}: {body}");
    }

    public sealed record ListeningSample(
        string VoiceId,
        string TextLanguage,
        string PromptLanguage,
        string ReferenceRelativePath,
        string GptWeightsRelativePath,
        string SovitsWeightsRelativePath,
        string PromptText,
        string SampleText,
        string OutputFileName);
}
