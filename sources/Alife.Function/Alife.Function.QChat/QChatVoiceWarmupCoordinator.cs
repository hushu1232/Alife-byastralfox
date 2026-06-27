using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alife.Function.Speech;

namespace Alife.Function.QChat;

public enum QChatVoiceWarmupState
{
    NotStarted,
    EndpointUnreachable,
    Warming,
    Ready,
    Failed,
    Cancelled
}

public sealed record QChatVoiceWarmupProfileStatus(
    string AgentId,
    long BotId,
    string VoiceId,
    string ApiBaseUrl,
    QChatVoiceWarmupState State,
    string Reason,
    string? OutputPath,
    DateTimeOffset? UpdatedAt);

public sealed class QChatVoiceWarmupCoordinator(
    GptSoVitsSpeechModel speechModel,
    Func<Uri, CancellationToken, Task<bool>> endpointProbe,
    Func<int, TimeSpan>? retryDelayProvider = null,
    Action<string, string, object?, Exception?>? diagnosticWriter = null)
{
    readonly ConcurrentDictionary<string, QChatVoiceWarmupProfileStatus> statuses = new(StringComparer.OrdinalIgnoreCase);
    readonly Func<int, TimeSpan> getRetryDelay = retryDelayProvider ?? (attempt => TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(attempt, 5)))));
    CancellationTokenSource? runCancellation;
    Task? runTask;

    public Task StartAsync(IReadOnlyList<QChatVoiceProfile> profiles, CancellationToken cancellationToken = default)
    {
        if (runTask is { IsCompleted: false })
            return Task.CompletedTask;

        runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runTask = Task.Run(() => RunAsync(profiles, runCancellation.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (runCancellation == null)
            return;

        await runCancellation.CancelAsync().ConfigureAwait(false);
        if (runTask != null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        runCancellation.Dispose();
        runCancellation = null;
        runTask = null;
    }

    public async Task WarmupOnceAsync(IReadOnlyList<QChatVoiceProfile> profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        foreach (QChatVoiceProfile profile in profiles.Where(static profile => profile.Enabled))
            await WarmupProfileAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public QChatVoiceWarmupProfileStatus GetStatus(string voiceId)
    {
        return statuses.TryGetValue(voiceId, out QChatVoiceWarmupProfileStatus? status)
            ? status
            : new QChatVoiceWarmupProfileStatus(
                AgentId: "",
                BotId: 0,
                VoiceId: voiceId,
                ApiBaseUrl: "",
                State: QChatVoiceWarmupState.NotStarted,
                Reason: "not_started",
                OutputPath: null,
                UpdatedAt: null);
    }

    async Task RunAsync(IReadOnlyList<QChatVoiceProfile> profiles, CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (cancellationToken.IsCancellationRequested == false)
        {
            await WarmupOnceAsync(profiles, cancellationToken).ConfigureAwait(false);
            if (profiles.Where(static profile => profile.Enabled).All(profile => GetStatus(profile.VoiceId).State == QChatVoiceWarmupState.Ready))
                return;

            attempt++;
            await Task.Delay(getRetryDelay(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    async Task WarmupProfileAsync(QChatVoiceProfile profile, CancellationToken cancellationToken)
    {
        Update(profile, QChatVoiceWarmupState.Warming, "warming", null);
        try
        {
            if (Uri.TryCreate(profile.ApiBaseUrl.TrimEnd('/'), UriKind.Absolute, out Uri? endpoint) == false ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                Update(profile, QChatVoiceWarmupState.Failed, "invalid_api_base_url", null);
                return;
            }

            if (await endpointProbe(endpoint, cancellationToken).ConfigureAwait(false) == false)
            {
                Update(profile, QChatVoiceWarmupState.EndpointUnreachable, "endpoint_unreachable", null);
                return;
            }

            GptSoVitsWarmupService service = new(speechModel);
            GptSoVitsWarmupResult result = await service.WarmupAsync(
                MapToGptSoVitsProfile(profile),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            Update(
                profile,
                result.Success ? QChatVoiceWarmupState.Ready : QChatVoiceWarmupState.Failed,
                result.Success ? "ready" : "warmup_failed",
                result.OutputPath);
        }
        catch (OperationCanceledException)
        {
            Update(profile, QChatVoiceWarmupState.Cancelled, "cancelled", null);
            throw;
        }
        catch (Exception ex)
        {
            diagnosticWriter?.Invoke(
                "qchat-voice-warmup-failed",
                "QChat voice warmup failed.",
                new { profile.AgentId, profile.BotId, profile.VoiceId },
                ex);
            Update(profile, QChatVoiceWarmupState.Failed, "warmup_exception", null);
        }
    }

    static GptSoVitsVoiceProfile MapToGptSoVitsProfile(QChatVoiceProfile profile)
    {
        return new GptSoVitsVoiceProfile
        {
            VoiceId = profile.VoiceId,
            AgentId = profile.AgentId,
            BotId = profile.BotId,
            ApiBaseUrl = profile.ApiBaseUrl,
            ReferenceAudioPath = profile.ReferenceAudioPath,
            GptWeightsPath = profile.GptWeightsPath,
            SovitsWeightsPath = profile.SovitsWeightsPath,
            PromptText = profile.PromptText,
            TextLanguage = profile.TextLanguage,
            PromptLanguage = profile.PromptLanguage,
            MaxTextChars = profile.MaxTextChars
        };
    }

    void Update(QChatVoiceProfile profile, QChatVoiceWarmupState state, string reason, string? outputPath)
    {
        statuses[profile.VoiceId] = new QChatVoiceWarmupProfileStatus(
            AgentId: profile.AgentId,
            BotId: profile.BotId,
            VoiceId: profile.VoiceId,
            ApiBaseUrl: profile.ApiBaseUrl,
            State: state,
            Reason: reason,
            OutputPath: outputPath,
            UpdatedAt: DateTimeOffset.UtcNow);
    }
}
