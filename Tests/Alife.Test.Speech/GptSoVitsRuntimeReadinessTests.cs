using Alife.Function.Speech;

namespace Alife.Test.Speech;

[TestFixture]
public sealed class GptSoVitsRuntimeReadinessTests
{
    [Test]
    public async Task EvaluateAsync_ReadyWhenReferenceExistsAndEndpointResponds()
    {
        string voiceRoot = CreateVoiceRoot();

        GptSoVitsRuntimeReadinessStatus status = await GptSoVitsRuntimeReadiness.EvaluateAsync(
            new GptSoVitsSpeechModelConfig
            {
                ApiBaseUrl = "http://127.0.0.1:9880",
                VoiceRootPath = voiceRoot,
                PromptText = "reference prompt"
            },
            (_, _) => Task.FromResult(true));

        Assert.Multiple(() =>
        {
            Assert.That(status.Ready, Is.True);
            Assert.That(status.Status, Is.EqualTo("ready"));
            Assert.That(status.Reason, Is.EqualTo("ready"));
            Assert.That(status.ReferenceAudioExists, Is.True);
            Assert.That(status.PromptTextConfigured, Is.True);
            Assert.That(status.EndpointReachable, Is.True);
        });
    }

    [Test]
    public async Task EvaluateAsync_MissingReferenceAudioBlocksReadiness()
    {
        string voiceRoot = CreateVoiceRoot(writeReferenceAudio: false);

        GptSoVitsRuntimeReadinessStatus status = await GptSoVitsRuntimeReadiness.EvaluateAsync(
            new GptSoVitsSpeechModelConfig
            {
                ApiBaseUrl = "http://127.0.0.1:9880",
                VoiceRootPath = voiceRoot
            },
            (_, _) => Task.FromResult(true));

        Assert.Multiple(() =>
        {
            Assert.That(status.Ready, Is.False);
            Assert.That(status.Status, Is.EqualTo("missing_reference_audio"));
            Assert.That(status.Reason, Is.EqualTo("reference_audio_missing"));
            Assert.That(status.ReferenceAudioExists, Is.False);
        });
    }

    [Test]
    public async Task EvaluateAsync_InvalidApiBaseUrlBlocksReadiness()
    {
        string voiceRoot = CreateVoiceRoot();

        GptSoVitsRuntimeReadinessStatus status = await GptSoVitsRuntimeReadiness.EvaluateAsync(
            new GptSoVitsSpeechModelConfig
            {
                ApiBaseUrl = "not a url",
                VoiceRootPath = voiceRoot
            },
            (_, _) => Task.FromResult(true));

        Assert.Multiple(() =>
        {
            Assert.That(status.Ready, Is.False);
            Assert.That(status.Status, Is.EqualTo("invalid_api_base_url"));
            Assert.That(status.Reason, Is.EqualTo("api_base_url_invalid"));
            Assert.That(status.EndpointReachable, Is.False);
        });
    }

    [Test]
    public async Task EvaluateAsync_UnreachableEndpointBlocksReadiness()
    {
        string voiceRoot = CreateVoiceRoot();

        GptSoVitsRuntimeReadinessStatus status = await GptSoVitsRuntimeReadiness.EvaluateAsync(
            new GptSoVitsSpeechModelConfig
            {
                ApiBaseUrl = "http://127.0.0.1:9880",
                VoiceRootPath = voiceRoot
            },
            (_, _) => Task.FromResult(false));

        Assert.Multiple(() =>
        {
            Assert.That(status.Ready, Is.False);
            Assert.That(status.Status, Is.EqualTo("endpoint_unreachable"));
            Assert.That(status.Reason, Is.EqualTo("gpt_sovits_endpoint_unreachable"));
            Assert.That(status.EndpointReachable, Is.False);
        });
    }

    static string CreateVoiceRoot(bool writeReferenceAudio = true)
    {
        string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "gpt-sovits-readiness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        if (writeReferenceAudio)
            File.WriteAllBytes(Path.Combine(folder, "ref.wav"), [0x52, 0x49, 0x46, 0x46]);

        File.WriteAllText(Path.Combine(folder, "ref.txt"), "reference prompt");
        return folder;
    }
}
