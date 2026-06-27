using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.Speech;

public sealed class GptSoVitsWarmupService(
    GptSoVitsSpeechModel speechModel,
    Func<TimeSpan>? elapsedProvider = null)
{
    public async Task<GptSoVitsWarmupResult> WarmupAsync(
        GptSoVitsVoiceProfile profile,
        string? warmupText = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        string text = string.IsNullOrWhiteSpace(warmupText)
            ? GetDefaultWarmupText(profile.TextLanguage)
            : warmupText.Trim();

        long startTimestamp = Stopwatch.GetTimestamp();
        string? outputPath = await speechModel.GenerateSpeechFileAsync(text, profile, cancellationToken);
        TimeSpan elapsed = elapsedProvider?.Invoke() ?? Stopwatch.GetElapsedTime(startTimestamp);

        return new GptSoVitsWarmupResult(
            Success: string.IsNullOrWhiteSpace(outputPath) == false,
            VoiceId: profile.VoiceId,
            TextLanguage: profile.TextLanguage,
            WarmupText: text,
            OutputPath: outputPath,
            Elapsed: elapsed);
    }

    static string GetDefaultWarmupText(string? language)
    {
        return NormalizeLanguage(language) == "ja" ? "はい。" : "好。";
    }

    static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "";

        return language.Trim().ToLowerInvariant() switch
        {
            "jp" or "jpn" or "japanese" => "ja",
            "cn" or "zho" or "chi" or "chinese" => "zh",
            string normalized => normalized
        };
    }
}

public sealed record GptSoVitsWarmupResult(
    bool Success,
    string VoiceId,
    string TextLanguage,
    string WarmupText,
    string? OutputPath,
    TimeSpan Elapsed);
