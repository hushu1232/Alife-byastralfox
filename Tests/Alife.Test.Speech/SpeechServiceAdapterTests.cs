using Alife.Function.FunctionCaller;
using Alife.Function.Speech;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alife.Test.Speech;

public class SpeechServiceAdapterTests
{
    [Test]
    public async Task SpeakAsync_UsesInjectedAudioPlayer()
    {
        FakeSpeechModel speechModel = new();
        FakeAudioPlayer audioPlayer = new();
        SpeechService service = new(null!, speechModel, new NullLogger<SpeechService>(), audioPlayer);

        await service.SpeakAsync(" hello ");
        await service.DisposeAsync();

        Assert.That(speechModel.Texts, Is.EqualTo(new[] { "hello" }));
        Assert.That(audioPlayer.PlayedFiles, Is.EqualTo(new[] { "fake.wav" }));
    }

    sealed class FakeSpeechModel : ISpeechModel
    {
        public List<string> Texts { get; } = new();

        public Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
        {
            Texts.Add(text);
            return Task.FromResult<string?>("fake.wav");
        }
    }

    sealed class FakeAudioPlayer : ISpeechAudioPlayer
    {
        public List<string> PlayedFiles { get; } = new();

        public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
        {
            PlayedFiles.Add(filePath);
            return Task.CompletedTask;
        }
    }
}
