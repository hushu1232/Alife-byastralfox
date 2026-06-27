using Alife.Function.Speech;
using System.Net;
using System.Text.Json;

namespace Alife.Test.Speech;

public class GptSoVitsSpeechModelTests
{
    [Test]
    public void Config_DefaultsTargetLocalApiAndXiayuVoice()
    {
        var config = new GptSoVitsSpeechModelConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config.ApiBaseUrl, Is.EqualTo("http://127.0.0.1:9880"));
            Assert.That(config.VoiceId, Is.EqualTo("xiayu"));
            Assert.That(config.TextLanguage, Is.EqualTo("zh"));
            Assert.That(config.PromptLanguage, Is.EqualTo("zh"));
            Assert.That(config.MediaType, Is.EqualTo("wav"));
            Assert.That(config.MaxTextChars, Is.EqualTo(120));
            Assert.That(config.EnableCache, Is.True);
            Assert.That(config.AllowPersonaFallbackToEdgeTts, Is.False);
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_EmptyText_ReturnsNullWithoutHttpCall()
    {
        var handler = new RecordingHandler();
        var model = CreateModel(handler, CreateVoiceFolder());

        string? result = await model.GenerateSpeechFileAsync("   ");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(handler.Calls, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_TextLongerThanMaxTextChars_ReturnsNullWithoutHttpCall()
    {
        var handler = new RecordingHandler();
        var model = CreateModel(handler, CreateVoiceFolder(), config => config.MaxTextChars = 3);

        string? result = await model.GenerateSpeechFileAsync("1234");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(handler.Calls, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_MissingReferenceAudio_ReturnsNullWithoutHttpCall()
    {
        var voiceFolder = CreateVoiceFolder(writeReferenceAudio: false);
        var handler = new RecordingHandler();
        var model = CreateModel(handler, voiceFolder);

        string? result = await model.GenerateSpeechFileAsync("hello");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(handler.Calls, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_SuccessfulWavResponse_WritesFileAndReturnsExistingPath()
    {
        var handler = new RecordingHandler();
        var model = CreateModel(handler, CreateVoiceFolder());

        string? result = await model.GenerateSpeechFileAsync("hello");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(File.Exists(result!), Is.True);
            Assert.That(new FileInfo(result!).Length, Is.GreaterThan(0));
            Assert.That(handler.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_SameTextTwice_ReturnsSameCachedPathWithoutSecondHttpCall()
    {
        var handler = new RecordingHandler();
        var model = CreateModel(handler, CreateVoiceFolder());

        string? first = await model.GenerateSpeechFileAsync("hello");
        string? second = await model.GenerateSpeechFileAsync("hello");

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(handler.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_ZeroByteCachedFile_RegeneratesInsteadOfReturningPoisonedCache()
    {
        var handler = new RecordingHandler
        {
            ResponseBytes = []
        };
        var model = CreateModel(handler, CreateVoiceFolder());

        string? first = await model.GenerateSpeechFileAsync("hello");

        handler.ResponseBytes = [0x52, 0x49, 0x46, 0x46, 0x01];
        string? second = await model.GenerateSpeechFileAsync("hello");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(new FileInfo(second!).Length, Is.GreaterThan(0));
            Assert.That(handler.Calls, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_UsesNewCacheEntryWhenReferenceAudioChanges()
    {
        var voiceFolder = CreateVoiceFolder();
        var refAudioPath = Path.Combine(voiceFolder, "ref.wav");
        var handler = new RecordingHandler();
        var model = CreateModel(handler, voiceFolder);

        string? first = await model.GenerateSpeechFileAsync("hello");

        File.WriteAllBytes(refAudioPath, [0x52, 0x49, 0x46, 0x46, 0x02, 0x03]);
        File.SetLastWriteTimeUtc(refAudioPath, DateTime.UtcNow.AddMinutes(1));

        string? second = await model.GenerateSpeechFileAsync("hello");

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(handler.Calls, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_PostBodyContainsRequiredFields()
    {
        var voiceFolder = CreateVoiceFolder(promptText: "reference prompt");
        var handler = new RecordingHandler();
        var model = CreateModel(handler, voiceFolder, config =>
        {
            config.TextLanguage = "en";
            config.PromptLanguage = "zh";
            config.BatchSize = 2;
            config.MediaType = "wav";
        });

        await model.GenerateSpeechFileAsync("hello");

        using JsonDocument document = JsonDocument.Parse(handler.Body);
        JsonElement root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("text").GetString(), Is.EqualTo("hello"));
            Assert.That(root.GetProperty("text_lang").GetString(), Is.EqualTo("en"));
            Assert.That(root.GetProperty("ref_audio_path").GetString(), Is.EqualTo(Path.Combine(voiceFolder, "ref.wav")));
            Assert.That(root.GetProperty("prompt_text").GetString(), Is.EqualTo("reference prompt"));
            Assert.That(root.GetProperty("prompt_lang").GetString(), Is.EqualTo("zh"));
            Assert.That(root.GetProperty("batch_size").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("media_type").GetString(), Is.EqualTo("wav"));
            Assert.That(root.GetProperty("streaming_mode").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_ConfiguredOggMediaType_StillRequestsWavAndReturnsWavPath()
    {
        var handler = new RecordingHandler();
        var model = CreateModel(handler, CreateVoiceFolder(), config => config.MediaType = "ogg");

        string? result = await model.GenerateSpeechFileAsync("hello");

        using JsonDocument document = JsonDocument.Parse(handler.Body);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.EndWith(".wav"));
            Assert.That(document.RootElement.GetProperty("media_type").GetString(), Is.EqualTo("wav"));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_WithVoiceProfile_UsesProfilePayloadValues()
    {
        var handler = new RecordingHandler();
        string voiceFolder = CreateVoiceFolder();
        string refAudio = Path.Combine(voiceFolder, "custom-ref.wav");
        await File.WriteAllBytesAsync(refAudio, [1, 2, 3, 4]);
        var model = CreateModel(handler, voiceFolder, config =>
        {
            config.ApiBaseUrl = "http://global.example";
            config.ReferenceAudioPath = Path.Combine(voiceFolder, "ref.wav");
            config.PromptText = "global prompt";
        });

        string? result = await model.GenerateSpeechFileAsync("hello", new GptSoVitsVoiceProfile
        {
            VoiceId = "mixu",
            AgentId = "mixu",
            BotId = 3340947887,
            ApiBaseUrl = "http://profile.example",
            ReferenceAudioPath = refAudio,
            PromptText = "profile prompt",
            TextLanguage = "ja",
            PromptLanguage = "ja",
            MaxTextChars = 20
        });

        using JsonDocument document = JsonDocument.Parse(handler.Body);
        JsonElement root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(handler.RequestUri, Is.EqualTo("http://profile.example/tts"));
            Assert.That(root.GetProperty("ref_audio_path").GetString(), Is.EqualTo(refAudio));
            Assert.That(root.GetProperty("prompt_text").GetString(), Is.EqualTo("profile prompt"));
            Assert.That(root.GetProperty("text_lang").GetString(), Is.EqualTo("ja"));
            Assert.That(root.GetProperty("prompt_lang").GetString(), Is.EqualTo("ja"));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_WithVoiceProfileWeights_SetsWeightsBeforeTtsRequest()
    {
        var handler = new RecordingHandler();
        string voiceFolder = CreateVoiceFolder();
        string refAudio = Path.Combine(voiceFolder, "ref.wav");
        var model = CreateModel(handler, voiceFolder);

        string? result = await model.GenerateSpeechFileAsync("hello", new GptSoVitsVoiceProfile
        {
            VoiceId = "xiayu-zh",
            ApiBaseUrl = "http://profile.example",
            ReferenceAudioPath = refAudio,
            PromptText = "profile prompt",
            GptWeightsPath = @"D:\models\xiayu\s1.ckpt",
            SovitsWeightsPath = @"D:\models\xiayu\s2.pth"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(handler.RequestUris, Has.Count.EqualTo(3));
            Assert.That(handler.RequestUris[0], Does.StartWith("http://profile.example/set_gpt_weights?"));
            Assert.That(handler.RequestUris[0], Does.Contain("weights_path="));
            Assert.That(handler.RequestUris[1], Does.StartWith("http://profile.example/set_sovits_weights?"));
            Assert.That(handler.RequestUris[1], Does.Contain("weights_path="));
            Assert.That(handler.RequestUris[2], Is.EqualTo("http://profile.example/tts"));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_SameApiBaseUrlSerializesConcurrentRequests()
    {
        var handler = new RecordingHandler
        {
            Delay = TimeSpan.FromMilliseconds(40)
        };
        string voiceFolder = CreateVoiceFolder();
        string refAudio = Path.Combine(voiceFolder, "ref.wav");
        var model = CreateModel(handler, voiceFolder, config => config.EnableCache = false);
        var profile = new GptSoVitsVoiceProfile
        {
            VoiceId = "xiayu-zh",
            ApiBaseUrl = "http://same.example",
            ReferenceAudioPath = refAudio,
            PromptText = "same prompt"
        };

        await Task.WhenAll(
            model.GenerateSpeechFileAsync("hello one", profile),
            model.GenerateSpeechFileAsync("hello two", profile));

        Assert.Multiple(() =>
        {
            Assert.That(handler.Calls, Is.EqualTo(2));
            Assert.That(handler.MaxConcurrentCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_DifferentApiBaseUrlsAllowConcurrentRequests()
    {
        var handler = new RecordingHandler
        {
            Delay = TimeSpan.FromMilliseconds(80)
        };
        string voiceFolder = CreateVoiceFolder();
        string refAudio = Path.Combine(voiceFolder, "ref.wav");
        var model = CreateModel(handler, voiceFolder, config => config.EnableCache = false);
        var xiayu = new GptSoVitsVoiceProfile
        {
            VoiceId = "xiayu-zh",
            ApiBaseUrl = "http://127.0.0.1:9880",
            ReferenceAudioPath = refAudio,
            PromptText = "xiayu prompt"
        };
        var mixu = xiayu with
        {
            VoiceId = "mixu",
            ApiBaseUrl = "http://127.0.0.1:9881",
            PromptText = "mixu prompt"
        };

        await Task.WhenAll(
            model.GenerateSpeechFileAsync("hello xiayu", xiayu),
            model.GenerateSpeechFileAsync("hello mixu", mixu));

        Assert.Multiple(() =>
        {
            Assert.That(handler.Calls, Is.EqualTo(2));
            Assert.That(handler.MaxConcurrentCalls, Is.GreaterThanOrEqualTo(2));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_WithDifferentVoiceProfiles_DoesNotShareCache()
    {
        var handler = new RecordingHandler();
        string voiceFolder = CreateVoiceFolder();
        string xiayuRef = Path.Combine(voiceFolder, "xiayu.wav");
        string mixuRef = Path.Combine(voiceFolder, "mixu.wav");
        await File.WriteAllBytesAsync(xiayuRef, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(mixuRef, [5, 6, 7, 8]);
        var model = CreateModel(handler, voiceFolder);

        string? xiayu = await model.GenerateSpeechFileAsync("hello", new GptSoVitsVoiceProfile
        {
            VoiceId = "xiayu",
            AgentId = "xiayu",
            BotId = 2905391496,
            ReferenceAudioPath = xiayuRef,
            PromptText = "xiayu prompt"
        });
        string? mixu = await model.GenerateSpeechFileAsync("hello", new GptSoVitsVoiceProfile
        {
            VoiceId = "mixu",
            AgentId = "mixu",
            BotId = 3340947887,
            ReferenceAudioPath = mixuRef,
            PromptText = "mixu prompt"
        });

        Assert.Multiple(() =>
        {
            Assert.That(xiayu, Is.Not.Null);
            Assert.That(mixu, Is.Not.Null);
            Assert.That(mixu, Is.Not.EqualTo(xiayu));
            Assert.That(handler.Calls, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_DifferentProfileApiBaseUrls_DoesNotShareCache()
    {
        var handler = new RecordingHandler();
        string voiceFolder = CreateVoiceFolder();
        string refAudio = Path.Combine(voiceFolder, "ref.wav");
        var model = CreateModel(handler, voiceFolder);

        var firstProfile = new GptSoVitsVoiceProfile
        {
            VoiceId = "xiayu",
            ApiBaseUrl = "http://server-one.example/",
            ReferenceAudioPath = refAudio,
            PromptText = "same prompt"
        };
        var secondProfile = firstProfile with
        {
            ApiBaseUrl = "http://server-two.example"
        };

        string? first = await model.GenerateSpeechFileAsync("hello", firstProfile);
        string? second = await model.GenerateSpeechFileAsync("hello", secondProfile);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(handler.Calls, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GenerateSpeechFileAsync_WithVoiceProfileTooLong_ReturnsNullWithoutHttpCall()
    {
        var handler = new RecordingHandler();
        string voiceFolder = CreateVoiceFolder();
        var model = CreateModel(handler, voiceFolder);

        string? result = await model.GenerateSpeechFileAsync("1234", new GptSoVitsVoiceProfile
        {
            VoiceId = "xiayu",
            ReferenceAudioPath = Path.Combine(voiceFolder, "ref.wav"),
            MaxTextChars = 3
        });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(handler.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task Dispose_DoesNotDisposeInjectedHttpClient()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var model = new GptSoVitsSpeechModel(httpClient: httpClient)
        {
            Configuration = new GptSoVitsSpeechModelConfig
            {
                ApiBaseUrl = "http://localhost:9880",
                VoiceRootPath = CreateVoiceFolder()
            }
        };

        ((IDisposable)model).Dispose();
        using HttpResponseMessage response = await httpClient.GetAsync("http://localhost:9880/ping");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    static GptSoVitsSpeechModel CreateModel(
        RecordingHandler handler,
        string voiceFolder,
        Action<GptSoVitsSpeechModelConfig>? configure = null)
    {
        var config = new GptSoVitsSpeechModelConfig
        {
            ApiBaseUrl = "http://localhost:9880",
            VoiceRootPath = voiceFolder,
            EnableCache = true
        };
        configure?.Invoke(config);

        var model = new GptSoVitsSpeechModel(httpClient: new HttpClient(handler))
        {
            Configuration = config
        };
        return model;
    }

    static string CreateVoiceFolder(bool writeReferenceAudio = true, string promptText = "")
    {
        string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "gpt-sovits-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        if (writeReferenceAudio)
            File.WriteAllBytes(Path.Combine(folder, "ref.wav"), [0x52, 0x49, 0x46, 0x46]);

        if (promptText.Length > 0)
            File.WriteAllText(Path.Combine(folder, "ref.txt"), promptText);

        return folder;
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        int activeCalls;
        public int Calls { get; private set; }
        public int MaxConcurrentCalls { get; private set; }
        public string Body { get; private set; } = "";
        public string RequestUri { get; private set; } = "";
        public List<string> RequestUris { get; } = new();
        public byte[] ResponseBytes { get; set; } = [0x52, 0x49, 0x46, 0x46, 0x01];
        public TimeSpan Delay { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            int current = Interlocked.Increment(ref activeCalls);
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, current);
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestUri = request.RequestUri?.ToString() ?? "";
            RequestUris.Add(RequestUri);
            try
            {
                if (Delay > TimeSpan.Zero)
                    await Task.Delay(Delay, cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(ResponseBytes)
                };
            }
            finally
            {
                Interlocked.Decrement(ref activeCalls);
            }
        }
    }
}
