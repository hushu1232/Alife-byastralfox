using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alife.Framework;
using Alife.Platform;
using Microsoft.Extensions.Logging;

namespace Alife.Function.Speech;

[Module("MiMo Design TTS", "Governed MiMo voice-design speech with local GPT-SoVITS fallback.",
    defaultCategory: "astralfox-alife/Models/Speech")]
public sealed class MiMoDesignSpeechModel : ISpeechModel, IConfigurable<MiMoDesignSpeechModelConfig>, IDisposable
{
    readonly HttpClient httpClient;
    readonly ILogger<MiMoDesignSpeechModel>? logger;
    readonly bool ownsHttpClient;

    public MiMoDesignSpeechModel(ILogger<MiMoDesignSpeechModel>? logger = null, HttpClient? httpClient = null)
    {
        this.logger = logger;
        this.httpClient = httpClient ?? new HttpClient();
        ownsHttpClient = httpClient == null;
    }

    public MiMoDesignSpeechModelConfig? Configuration { get; set; }

    public async Task<string?> GenerateSpeechFileAsync(string text, CancellationToken cancellationToken = default)
    {
        MiMoDesignSpeechModelConfig config = Configuration ?? new();
        string trimmedText = (text ?? string.Empty).Trim();
        if (trimmedText.Length == 0 || trimmedText.Length > config.MaxTextChars)
            return null;

        try
        {
            byte[]? wav = await RequestWavAsync(config, trimmedText, cancellationToken);
            if (wav != null)
            {
                string outputFolder = Path.Combine(AlifePath.TempFolderPath, "MiMoDesignTTS");
                Directory.CreateDirectory(outputFolder);
                string outputPath = Path.Combine(outputFolder, Guid.NewGuid().ToString("N") + ".wav");
                await File.WriteAllBytesAsync(outputPath, wav, cancellationToken);
                return outputPath;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "MiMo Design TTS request failed; using local fallback when available.");
        }

        var fallback = new GptSoVitsSpeechModel(httpClient: httpClient)
        {
            Configuration = config.LocalFallback ?? new GptSoVitsSpeechModelConfig()
        };
        return await fallback.GenerateSpeechFileAsync(trimmedText, cancellationToken);
    }

    async Task<byte[]?> RequestWavAsync(
        MiMoDesignSpeechModelConfig config,
        string text,
        CancellationToken cancellationToken)
    {
        string voiceDesignDescription = config.DefaultVoiceDesignDescription?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(config.ApiKey) || voiceDesignDescription.Length == 0)
            return null;
        if (Uri.TryCreate(config.ApiEndpoint, UriKind.Absolute, out Uri? endpoint) == false ||
            (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
            return null;

        var payload = new
        {
            model = config.Model,
            audio = new { format = "wav" },
            messages = new[]
            {
                new { role = "user", content = voiceDesignDescription },
                new { role = "assistant", content = text }
            }
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (config.TimeoutSeconds > 0)
            timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("api-key", config.ApiKey);
        using HttpResponseMessage response = await httpClient.SendAsync(request, timeout.Token);
        if (response.IsSuccessStatusCode == false)
            return null;

        string json = await response.Content.ReadAsStringAsync(timeout.Token);
        using JsonDocument responseJson = JsonDocument.Parse(json);
        if (responseJson.RootElement.TryGetProperty("choices", out JsonElement choices) == false ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return null;
        if (choices[0].TryGetProperty("message", out JsonElement message) == false ||
            message.TryGetProperty("audio", out JsonElement audio) == false ||
            audio.TryGetProperty("data", out JsonElement data) == false)
            return null;
        string? base64 = data.GetString();
        if (string.IsNullOrWhiteSpace(base64))
            return null;
        byte[] wav;
        try { wav = Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }
        return IsWav(wav) ? wav : null;
    }

    static bool IsWav(byte[] bytes) => bytes.Length >= 12 &&
        bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
        bytes[8] == (byte)'W' && bytes[9] == (byte)'A' && bytes[10] == (byte)'V' && bytes[11] == (byte)'E';

    public void Dispose()
    {
        if (ownsHttpClient)
            httpClient.Dispose();
    }
}
