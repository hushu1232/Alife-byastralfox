using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Alife.Platform;

namespace Alife.Function.Speech;

public sealed record GptSoVitsRuntimeReadinessStatus(
    bool Ready,
    string Status,
    string Reason,
    string ApiBaseUrl,
    string ReferenceAudioPath,
    bool ReferenceAudioExists,
    bool PromptTextConfigured,
    bool EndpointReachable);

public static class GptSoVitsRuntimeReadiness
{
    public static async Task<GptSoVitsRuntimeReadinessStatus> EvaluateAsync(
        GptSoVitsSpeechModelConfig config,
        Func<Uri, CancellationToken, Task<bool>>? endpointProbe = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        string apiBaseUrl = string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            ? "http://127.0.0.1:9880"
            : config.ApiBaseUrl.Trim();
        string voiceRoot = ResolveVoiceRootPath(config);
        string referenceAudioPath = ResolveReferenceAudioPath(config, voiceRoot);
        bool referenceAudioExists = File.Exists(referenceAudioPath);
        bool promptTextConfigured = HasPromptText(config, voiceRoot);

        if (Uri.TryCreate(apiBaseUrl.TrimEnd('/'), UriKind.Absolute, out Uri? endpointUri) == false ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            return Create(
                ready: false,
                status: "invalid_api_base_url",
                reason: "api_base_url_invalid",
                apiBaseUrl,
                referenceAudioPath,
                referenceAudioExists,
                promptTextConfigured,
                endpointReachable: false);
        }

        if (referenceAudioExists == false)
        {
            return Create(
                ready: false,
                status: "missing_reference_audio",
                reason: "reference_audio_missing",
                apiBaseUrl,
                referenceAudioPath,
                referenceAudioExists,
                promptTextConfigured,
                endpointReachable: false);
        }

        bool endpointReachable = endpointProbe == null ||
            await endpointProbe(endpointUri, cancellationToken).ConfigureAwait(false);

        if (endpointReachable == false)
        {
            return Create(
                ready: false,
                status: "endpoint_unreachable",
                reason: "gpt_sovits_endpoint_unreachable",
                apiBaseUrl,
                referenceAudioPath,
                referenceAudioExists,
                promptTextConfigured,
                endpointReachable: false);
        }

        return Create(
            ready: true,
            status: "ready",
            reason: "ready",
            apiBaseUrl,
            referenceAudioPath,
            referenceAudioExists,
            promptTextConfigured,
            endpointReachable: true);
    }

    static GptSoVitsRuntimeReadinessStatus Create(
        bool ready,
        string status,
        string reason,
        string apiBaseUrl,
        string referenceAudioPath,
        bool referenceAudioExists,
        bool promptTextConfigured,
        bool endpointReachable)
    {
        return new GptSoVitsRuntimeReadinessStatus(
            ready,
            status,
            reason,
            apiBaseUrl,
            referenceAudioPath,
            referenceAudioExists,
            promptTextConfigured,
            endpointReachable);
    }

    static string ResolveVoiceRootPath(GptSoVitsSpeechModelConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.VoiceRootPath) == false)
            return config.VoiceRootPath.Trim();

        string voiceId = string.IsNullOrWhiteSpace(config.VoiceId) ? "xiayu" : config.VoiceId.Trim();
        return Path.Combine(AlifePath.RuntimeFolderPath, "TTS", "voices", voiceId);
    }

    static string ResolveReferenceAudioPath(GptSoVitsSpeechModelConfig config, string voiceRootPath)
    {
        return string.IsNullOrWhiteSpace(config.ReferenceAudioPath)
            ? Path.Combine(voiceRootPath, "ref.wav")
            : config.ReferenceAudioPath.Trim();
    }

    static bool HasPromptText(GptSoVitsSpeechModelConfig config, string voiceRootPath)
    {
        if (string.IsNullOrWhiteSpace(config.PromptText) == false)
            return true;

        string promptPath = Path.Combine(voiceRootPath, "ref.txt");
        return File.Exists(promptPath) && string.IsNullOrWhiteSpace(File.ReadAllText(promptPath)) == false;
    }
}
