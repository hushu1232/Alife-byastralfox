using Alife.Function.QChat;
using Alife.Function.Speech;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatVoiceWarmupCoordinatorTests
{
    [Test]
    public async Task WarmupAsync_ReachableEndpointMarksProfileReady()
    {
        QChatVoiceProfile profile = CreateProfile("xiayu", 2905391496, "xiayu-zh");
        FakeWarmupSpeechModel speechModel = new("warmup.wav");
        QChatVoiceWarmupCoordinator coordinator = new(
            speechModel,
            (_, _) => Task.FromResult(true),
            retryDelayProvider: _ => TimeSpan.Zero);

        await coordinator.WarmupOnceAsync([profile], CancellationToken.None);

        QChatVoiceWarmupProfileStatus status = coordinator.GetStatus(profile.VoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(status.State, Is.EqualTo(QChatVoiceWarmupState.Ready));
            Assert.That(status.AgentId, Is.EqualTo("xiayu"));
            Assert.That(status.BotId, Is.EqualTo(2905391496));
            Assert.That(status.OutputPath, Is.EqualTo("warmup.wav"));
        });
    }

    [Test]
    public async Task WarmupAsync_UnreachableEndpointMarksProfileUnreachableWithoutSynthesis()
    {
        QChatVoiceProfile profile = CreateProfile("mixu", 3340947887, "mixu-zh");
        FakeWarmupSpeechModel speechModel = new("warmup.wav");
        QChatVoiceWarmupCoordinator coordinator = new(
            speechModel,
            (_, _) => Task.FromResult(false),
            retryDelayProvider: _ => TimeSpan.Zero);

        await coordinator.WarmupOnceAsync([profile], CancellationToken.None);

        QChatVoiceWarmupProfileStatus status = coordinator.GetStatus(profile.VoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(status.State, Is.EqualTo(QChatVoiceWarmupState.EndpointUnreachable));
            Assert.That(speechModel.Requests, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task WarmupAsync_MultipleProfilesTrackIndependentStatuses()
    {
        QChatVoiceProfile xiayu = CreateProfile("xiayu", 2905391496, "xiayu-zh");
        QChatVoiceProfile mixu = CreateProfile("mixu", 3340947887, "mixu-zh");
        FakeWarmupSpeechModel speechModel = new("warmup.wav");
        QChatVoiceWarmupCoordinator coordinator = new(
            speechModel,
            (_, _) => Task.FromResult(true),
            retryDelayProvider: _ => TimeSpan.Zero);

        await coordinator.WarmupOnceAsync([xiayu, mixu], CancellationToken.None);

        QChatVoiceWarmupProfileStatus xiayuStatus = coordinator.GetStatus(xiayu.VoiceId);
        QChatVoiceWarmupProfileStatus mixuStatus = coordinator.GetStatus(mixu.VoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(xiayuStatus.State, Is.EqualTo(QChatVoiceWarmupState.Ready));
            Assert.That(xiayuStatus.AgentId, Is.EqualTo("xiayu"));
            Assert.That(xiayuStatus.BotId, Is.EqualTo(2905391496));
            Assert.That(xiayuStatus.OutputPath, Is.EqualTo("warmup.wav"));
            Assert.That(mixuStatus.State, Is.EqualTo(QChatVoiceWarmupState.Ready));
            Assert.That(mixuStatus.AgentId, Is.EqualTo("mixu"));
            Assert.That(mixuStatus.BotId, Is.EqualTo(3340947887));
            Assert.That(mixuStatus.OutputPath, Is.EqualTo("warmup.wav"));
            Assert.That(speechModel.Requests, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task StartAsync_ReturnsBeforeWarmupCompletes()
    {
        QChatVoiceProfile profile = CreateProfile("xiayu", 2905391496, "xiayu-zh");
        TaskCompletionSource<bool> probeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> allowProbe = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeWarmupSpeechModel speechModel = new("warmup.wav");
        QChatVoiceWarmupCoordinator coordinator = new(
            speechModel,
            async (_, _) =>
            {
                probeStarted.SetResult(true);
                await allowProbe.Task;
                return true;
            },
            retryDelayProvider: _ => TimeSpan.Zero);

        await coordinator.StartAsync([profile], CancellationToken.None);
        await probeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(coordinator.GetStatus(profile.VoiceId).State, Is.EqualTo(QChatVoiceWarmupState.Warming));
        allowProbe.SetResult(true);
        await coordinator.StopAsync();
    }

    [Test]
    public async Task StartAsync_RetriesUntilEndpointBecomesReachable()
    {
        QChatVoiceProfile profile = CreateProfile("xiayu", 2905391496, "xiayu-zh");
        TaskCompletionSource<bool> retryObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeWarmupSpeechModel speechModel = new("warmup.wav");
        int probes = 0;
        QChatVoiceWarmupCoordinator coordinator = new(
            speechModel,
            (_, _) =>
            {
                int probe = Interlocked.Increment(ref probes);
                if (probe == 1)
                    return Task.FromResult(false);

                retryObserved.TrySetResult(true);
                return Task.FromResult(true);
            },
            retryDelayProvider: _ => TimeSpan.Zero);

        await coordinator.StartAsync([profile], CancellationToken.None);
        await retryObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => coordinator.GetStatus(profile.VoiceId).State == QChatVoiceWarmupState.Ready);

        QChatVoiceWarmupProfileStatus status = coordinator.GetStatus(profile.VoiceId);
        Assert.Multiple(() =>
        {
            Assert.That(probes, Is.GreaterThanOrEqualTo(2));
            Assert.That(status.State, Is.EqualTo(QChatVoiceWarmupState.Ready));
            Assert.That(status.Reason, Is.EqualTo("ready"));
            Assert.That(speechModel.Requests, Is.EqualTo(1));
        });
        await coordinator.StopAsync();
    }

    static QChatVoiceProfile CreateProfile(string agentId, long botId, string voiceId)
    {
        return new QChatVoiceProfile
        {
            Enabled = true,
            AgentId = agentId,
            BotId = botId,
            VoiceId = voiceId,
            ApiBaseUrl = "http://127.0.0.1:9880",
            ReferenceAudioPath = "ref.wav",
            PromptText = "reference",
            TextLanguage = "zh",
            PromptLanguage = "zh"
        };
    }

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (condition() == false)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, timeout.Token);
        }
    }

    sealed class FakeWarmupSpeechModel(string outputPath) : GptSoVitsSpeechModel
    {
        public int Requests { get; private set; }

        public override Task<string?> GenerateSpeechFileAsync(
            string text,
            GptSoVitsVoiceProfile profile,
            CancellationToken cancellationToken = default)
        {
            Requests++;
            return Task.FromResult<string?>(outputPath);
        }
    }
}
