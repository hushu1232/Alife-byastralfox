using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Platform;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Speech;

[Module("GPT-SoVITS语音克隆（待完成）", "实验中的GPT-SoVITS HTTP API语音克隆引擎；默认不应作为生产语音能力启用",
    defaultCategory: "astralfox-alife/模型接入/语音模型",
    EditorUI = typeof(GptSoVitsSpeechModelUI))]
public class GptSoVitsSpeechModel :
    ISpeechModel,
    IConfigurable<GptSoVitsSpeechModelConfig>,
    IDisposable
{
    const string ProviderMediaType = "wav";
    static readonly ConcurrentDictionary<string, SemaphoreSlim> EndpointGates = new(StringComparer.OrdinalIgnoreCase);

    readonly ILogger<GptSoVitsSpeechModel>? logger;
    readonly HttpClient httpClient;
    readonly bool ownsHttpClient;

    public GptSoVitsSpeechModel(
        ILogger<GptSoVitsSpeechModel>? logger = null,
        HttpClient? httpClient = null)
    {
        this.logger = logger;
        this.httpClient = httpClient ?? new HttpClient();
        ownsHttpClient = httpClient == null;
    }

    public GptSoVitsSpeechModelConfig? Configuration { get; set; }

    public Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        return GenerateSpeechFileCoreAsync(text, Configuration ?? new GptSoVitsSpeechModelConfig(), cancellationToken);
    }

    public virtual Task<string?> GenerateSpeechFileAsync(
        string text,
        GptSoVitsVoiceProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return GenerateSpeechFileCoreAsync(text, CreateEffectiveConfig(profile), cancellationToken);
    }

    async Task<string?> GenerateSpeechFileCoreAsync(
        string text,
        GptSoVitsSpeechModelConfig config,
        CancellationToken cancellationToken)
    {
        string trimmedText = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmedText))
            return null;

        if (trimmedText.Length > config.MaxTextChars)
            return null;

        string voiceRootPath = ResolveVoiceRootPath(config);
        string refAudioPath = ResolveReferenceAudioPath(config, voiceRootPath);
        if (File.Exists(refAudioPath) == false)
        {
            logger?.LogWarning("GPT-SoVITS reference audio does not exist: {ReferenceAudioPath}", refAudioPath);
            return null;
        }

        try
        {
            string promptText = ResolvePromptText(config, voiceRootPath);
            RefAudioStamp refAudioStamp = GetRefAudioStamp(refAudioPath);

            string outputDirectory = Path.Combine(AlifePath.TempFolderPath, "GPT-SoVITS");
            Directory.CreateDirectory(outputDirectory);

            string outputPath = config.EnableCache
                ? Path.Combine(outputDirectory, ComputeCacheKey(config, trimmedText, promptText, refAudioPath, refAudioStamp) + ".wav")
                : Path.Combine(outputDirectory, Guid.NewGuid().ToString("N") + ".wav");

            if (config.EnableCache && IsUsableAudioFile(outputPath))
                return outputPath;

            if (config.EnableCache && File.Exists(outputPath))
                TryDeleteFile(outputPath);

            string apiBaseUrl = config.ApiBaseUrl.TrimEnd('/');
            SemaphoreSlim endpointGate = EndpointGates.GetOrAdd(apiBaseUrl, _ => new SemaphoreSlim(1, 1));
            await endpointGate.WaitAsync(cancellationToken);
            try
            {
                if (config.EnableCache && IsUsableAudioFile(outputPath))
                    return outputPath;

                if (await SetWeightsIfConfiguredAsync(config, apiBaseUrl, cancellationToken) == false)
                    return null;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (config.TimeoutSeconds > 0)
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{apiBaseUrl}/tts")
                {
                    Content = CreateRequestContent(config, trimmedText, promptText, refAudioPath)
                };

                using HttpResponseMessage response = await httpClient.SendAsync(request, timeoutCts.Token);
                if (response.IsSuccessStatusCode == false)
                {
                    logger?.LogWarning("GPT-SoVITS request failed with HTTP status {StatusCode}.", (int)response.StatusCode);
                    return null;
                }

                byte[] bytes = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
                await File.WriteAllBytesAsync(outputPath, bytes, cancellationToken);

                if (IsUsableAudioFile(outputPath))
                    return outputPath;

                return null;
            }
            finally
            {
                endpointGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger?.LogWarning(ex, "GPT-SoVITS request timed out.");
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "GPT-SoVITS speech generation failed.");
            return null;
        }
    }

    public void Dispose()
    {
        if (ownsHttpClient)
            httpClient.Dispose();
    }

    static string ResolveVoiceRootPath(GptSoVitsSpeechModelConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.VoiceRootPath) == false)
            return config.VoiceRootPath;

        return Path.Combine(AlifePath.RuntimeFolderPath, "TTS", "voices", config.VoiceId);
    }

    static string ResolveReferenceAudioPath(GptSoVitsSpeechModelConfig config, string voiceRootPath)
    {
        if (string.IsNullOrWhiteSpace(config.ReferenceAudioPath) == false)
            return config.ReferenceAudioPath;

        return Path.Combine(voiceRootPath, "ref.wav");
    }

    static string ResolvePromptText(GptSoVitsSpeechModelConfig config, string voiceRootPath)
    {
        if (string.IsNullOrWhiteSpace(config.PromptText) == false)
            return config.PromptText;

        string promptPath = Path.Combine(voiceRootPath, "ref.txt");
        return File.Exists(promptPath) ? File.ReadAllText(promptPath) : "";
    }

    GptSoVitsSpeechModelConfig CreateEffectiveConfig(GptSoVitsVoiceProfile profile)
    {
        GptSoVitsSpeechModelConfig baseConfig = Configuration ?? new GptSoVitsSpeechModelConfig();
        return new GptSoVitsSpeechModelConfig
        {
            ApiBaseUrl = string.IsNullOrWhiteSpace(profile.ApiBaseUrl) ? baseConfig.ApiBaseUrl : profile.ApiBaseUrl,
            VoiceId = string.IsNullOrWhiteSpace(profile.VoiceId) ? baseConfig.VoiceId : profile.VoiceId,
            VoiceRootPath = string.IsNullOrWhiteSpace(profile.VoiceRootPath) ? baseConfig.VoiceRootPath : profile.VoiceRootPath,
            ReferenceAudioPath = string.IsNullOrWhiteSpace(profile.ReferenceAudioPath)
                ? baseConfig.ReferenceAudioPath
                : profile.ReferenceAudioPath,
            GptWeightsPath = string.IsNullOrWhiteSpace(profile.GptWeightsPath)
                ? baseConfig.GptWeightsPath
                : profile.GptWeightsPath,
            SovitsWeightsPath = string.IsNullOrWhiteSpace(profile.SovitsWeightsPath)
                ? baseConfig.SovitsWeightsPath
                : profile.SovitsWeightsPath,
            PromptText = string.IsNullOrWhiteSpace(profile.PromptText) ? baseConfig.PromptText : profile.PromptText,
            TextLanguage = string.IsNullOrWhiteSpace(profile.TextLanguage) ? baseConfig.TextLanguage : profile.TextLanguage,
            PromptLanguage = string.IsNullOrWhiteSpace(profile.PromptLanguage)
                ? baseConfig.PromptLanguage
                : profile.PromptLanguage,
            TextSplitMethod = baseConfig.TextSplitMethod,
            MediaType = baseConfig.MediaType,
            MaxTextChars = profile.MaxTextChars ?? baseConfig.MaxTextChars,
            TimeoutSeconds = baseConfig.TimeoutSeconds,
            BatchSize = baseConfig.BatchSize,
            SpeedFactor = baseConfig.SpeedFactor,
            TopK = baseConfig.TopK,
            TopP = baseConfig.TopP,
            Temperature = baseConfig.Temperature,
            ParallelInfer = baseConfig.ParallelInfer,
            RepetitionPenalty = baseConfig.RepetitionPenalty,
            EnableCache = baseConfig.EnableCache,
            AllowPersonaFallbackToEdgeTts = baseConfig.AllowPersonaFallbackToEdgeTts
        };
    }

    async Task<bool> SetWeightsIfConfiguredAsync(
        GptSoVitsSpeechModelConfig config,
        string apiBaseUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.GptWeightsPath) == false &&
            await SetWeightsAsync(apiBaseUrl, "set_gpt_weights", "weights_path", config.GptWeightsPath, cancellationToken) == false)
            return false;

        if (string.IsNullOrWhiteSpace(config.SovitsWeightsPath) == false &&
            await SetWeightsAsync(apiBaseUrl, "set_sovits_weights", "weights_path", config.SovitsWeightsPath, cancellationToken) == false)
            return false;

        return true;
    }

    async Task<bool> SetWeightsAsync(
        string apiBaseUrl,
        string endpoint,
        string parameterName,
        string weightsPath,
        CancellationToken cancellationToken)
    {
        string requestUri = $"{apiBaseUrl}/{endpoint}?{parameterName}={Uri.EscapeDataString(weightsPath)}";
        using HttpResponseMessage response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (response.IsSuccessStatusCode)
            return true;

        logger?.LogWarning(
            "GPT-SoVITS {Endpoint} failed with HTTP status {StatusCode}.",
            endpoint,
            (int)response.StatusCode);
        return false;
    }

    static HttpContent CreateRequestContent(
        GptSoVitsSpeechModelConfig config,
        string text,
        string promptText,
        string refAudioPath)
    {
        var payload = new
        {
            text,
            text_lang = config.TextLanguage,
            ref_audio_path = refAudioPath,
            aux_ref_audio_paths = Array.Empty<string>(),
            prompt_text = promptText,
            prompt_lang = config.PromptLanguage,
            top_k = config.TopK,
            top_p = config.TopP,
            temperature = config.Temperature,
            text_split_method = config.TextSplitMethod,
            batch_size = config.BatchSize,
            batch_threshold = 0.75,
            split_bucket = true,
            speed_factor = config.SpeedFactor,
            fragment_interval = 0.3,
            seed = -1,
            media_type = ProviderMediaType,
            streaming_mode = false,
            parallel_infer = config.ParallelInfer,
            repetition_penalty = config.RepetitionPenalty
        };

        string json = JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    static string ComputeCacheKey(
        GptSoVitsSpeechModelConfig config,
        string text,
        string promptText,
        string refAudioPath,
        RefAudioStamp refAudioStamp)
    {
        string stableInput = string.Join("\n",
        [
            "gpt-sovits-http",
            config.ApiBaseUrl.TrimEnd('/'),
            config.VoiceId,
            text,
            promptText,
            refAudioPath,
            refAudioStamp.Length.ToString(CultureInfo.InvariantCulture),
            refAudioStamp.LastWriteTimeUtcTicks.ToString(CultureInfo.InvariantCulture),
            config.GptWeightsPath,
            config.SovitsWeightsPath,
            config.TextLanguage,
            config.PromptLanguage,
            config.TextSplitMethod,
            config.BatchSize.ToString(CultureInfo.InvariantCulture),
            ProviderMediaType,
            config.SpeedFactor.ToString(CultureInfo.InvariantCulture),
            config.TopK.ToString(CultureInfo.InvariantCulture),
            config.TopP.ToString(CultureInfo.InvariantCulture),
            config.Temperature.ToString(CultureInfo.InvariantCulture),
            config.ParallelInfer.ToString(CultureInfo.InvariantCulture),
            config.RepetitionPenalty.ToString(CultureInfo.InvariantCulture)
        ]);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(stableInput));
        return Convert.ToHexString(hash);
    }

    static bool IsUsableAudioFile(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    static RefAudioStamp GetRefAudioStamp(string refAudioPath)
    {
        var fileInfo = new FileInfo(refAudioPath);
        return new RefAudioStamp(fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks);
    }

    void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to delete unusable GPT-SoVITS cache file: {OutputPath}", path);
        }
    }

    readonly record struct RefAudioStamp(long Length, long LastWriteTimeUtcTicks);
}
