using Alife.Function.Speech;

namespace Alife.Test.Speech;

[TestFixture]
public sealed class GptSoVitsWarmupServiceTests
{
    [Test]
    public async Task WarmupAsync_GeneratesWarmupTextWithProfileAndReturnsTiming()
    {
        var model = new RecordingSpeechModel("D:\\temp\\warmup.wav");
        var service = new GptSoVitsWarmupService(model, () => TimeSpan.FromMilliseconds(42));
        var profile = new GptSoVitsVoiceProfile
        {
            VoiceId = "xiayu-zh",
            AgentId = "xiayu",
            BotId = 2905391496,
            ReferenceAudioPath = "D:\\voices\\xiayu\\zh\\ref.wav",
            PromptText = "reference prompt",
            TextLanguage = "zh",
            PromptLanguage = "zh"
        };

        GptSoVitsWarmupResult result = await service.WarmupAsync(profile, "好。");

        Assert.Multiple(() =>
        {
            Assert.That(model.Text, Is.EqualTo("好。"));
            Assert.That(model.Profile, Is.SameAs(profile));
            Assert.That(result.Success, Is.True);
            Assert.That(result.VoiceId, Is.EqualTo("xiayu-zh"));
            Assert.That(result.TextLanguage, Is.EqualTo("zh"));
            Assert.That(result.OutputPath, Is.EqualTo("D:\\temp\\warmup.wav"));
            Assert.That(result.Elapsed, Is.EqualTo(TimeSpan.FromMilliseconds(42)));
        });
    }

    [Test]
    public async Task WarmupAsync_UsesLanguageDefaultTextWhenTextIsBlank()
    {
        var model = new RecordingSpeechModel("D:\\temp\\warmup.wav");
        var service = new GptSoVitsWarmupService(model, () => TimeSpan.FromMilliseconds(1));
        var profile = new GptSoVitsVoiceProfile
        {
            VoiceId = "xiayu-ja",
            TextLanguage = "ja",
            ReferenceAudioPath = "D:\\voices\\xiayu\\ja\\ref.wav"
        };

        await service.WarmupAsync(profile, "");

        Assert.That(model.Text, Is.EqualTo("はい。"));
    }

    [Test]
    public async Task WarmupAsync_ReturnsFailureWhenModelDoesNotGenerateFile()
    {
        var model = new RecordingSpeechModel(null);
        var service = new GptSoVitsWarmupService(model, () => TimeSpan.FromMilliseconds(7));

        GptSoVitsWarmupResult result = await service.WarmupAsync(new GptSoVitsVoiceProfile
        {
            VoiceId = "xiayu-zh",
            TextLanguage = "zh",
            ReferenceAudioPath = "D:\\voices\\xiayu\\zh\\ref.wav"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.OutputPath, Is.Null);
            Assert.That(result.Elapsed, Is.EqualTo(TimeSpan.FromMilliseconds(7)));
        });
    }

    sealed class RecordingSpeechModel(string? outputPath) : GptSoVitsSpeechModel
    {
        public string? Text { get; private set; }
        public GptSoVitsVoiceProfile? Profile { get; private set; }

        public override Task<string?> GenerateSpeechFileAsync(
            string text,
            GptSoVitsVoiceProfile profile,
            CancellationToken cancellationToken = default)
        {
            Text = text;
            Profile = profile;
            return Task.FromResult(outputPath);
        }
    }
}
