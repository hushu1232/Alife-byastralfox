using Alife.Function.QChat;
using NUnit.Framework;
using System.Text.Json;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatImageRecognitionServiceTests
{
    [Test]
    public async Task BuildsInternalPromptForAnalyzedPublicUrl()
    {
        FakeImageRecognitionClient client = new("a cat on a desk");
        QChatImageRecognitionService service = new(client);

        string? prompt = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            EnabledConfig(),
            Message("[CQ:image,file=cat.jpg,url=https://example.invalid/cat.jpg]", OneBotMessageType.Private),
            QChatSenderRole.Owner,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: false));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("[qchat image analysis]"));
            Assert.That(prompt, Does.Contain("provider=agnes"));
            Assert.That(prompt, Does.Contain("image_1_status=analyzed"));
            Assert.That(prompt, Does.Contain("image_1_summary=a cat on a desk"));
            Assert.That(prompt, Does.Not.Contain("https://example.invalid/cat.jpg"));
            Assert.That(client.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task BuildPromptAsync_UsesResolvedVisionProfileSettings()
    {
        FakeImageRecognitionClient client = new("profile image summary");
        QChatImageRecognitionService service = new(client);
        QChatConfig config = EnabledConfig();
        config.MaxImagesPerMessage = 1;
        QChatVisionProfile profile = new()
        {
            AgentId = "mixu",
            BotId = 3340947887,
            Model = "mixu-vision-model",
            ApiEndpoint = "https://vision.example.invalid/v1/chat/completions",
            MaxImagesPerMessage = 2
        };

        string? prompt = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            config,
            Message("[CQ:image,file=a.jpg,url=https://example.invalid/a.jpg][CQ:image,file=b.jpg,url=https://example.invalid/b.jpg]", OneBotMessageType.Private),
            QChatSenderRole.Owner,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: false,
            VisionProfile: profile));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("image_count=2"));
            Assert.That(client.Requests, Has.Count.EqualTo(2));
            Assert.That(client.Requests.Select(request => request.Model), Is.All.EqualTo("mixu-vision-model"));
            Assert.That(client.Requests.Select(request => request.ApiEndpoint), Is.All.EqualTo("https://vision.example.invalid/v1/chat/completions"));
        });
    }

    [Test]
    public async Task BuildPromptAsync_UsesGrokFallbackWithItsOwnProviderSettings()
    {
        FailingFakeImageRecognitionClient agnes = new(QChatImageRecognitionProviderResult.Fail(
            "agnes", "agnes-2.0-flash", QChatImageRecognitionFailureKind.Timeout, "timeout"));
        NamedFakeImageRecognitionClient grok = new("grok", "screenshot text");
        QChatVisionProviderCatalog catalog = new()
        {
            Providers =
            [
                new QChatVisionProviderSettings { ProviderId = "agnes", Model = "agnes-2.0-flash", ApiEndpoint = "https://agnes.example.invalid/v1" },
                new QChatVisionProviderSettings { ProviderId = "grok", Model = "grok-4.5", ApiEndpoint = "https://grok.example.invalid/v1" }
            ]
        };
        QChatImageRecognitionService service = new(
            new QChatVisionExecutionCoordinator(new Dictionary<string, IQChatImageRecognitionClient>
            {
                ["agnes"] = agnes,
                ["grok"] = grok
            }),
            catalog);
        QChatVisionProfile profile = new()
        {
            PrimaryProvider = "agnes",
            FallbackProvider = "grok",
            ComplexRequestProvider = "grok"
        };

        string? prompt = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            EnabledConfig(),
            Message("[CQ:image,file=screen.jpg,url=https://example.invalid/screen.jpg]", OneBotMessageType.Private),
            QChatSenderRole.Owner,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: false,
            VisionProfile: profile));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("provider=grok"));
            Assert.That(prompt, Does.Contain("image_1_summary=screenshot text"));
            Assert.That(agnes.Calls, Is.EqualTo(1));
            Assert.That(grok.Calls, Is.EqualTo(1));
            Assert.That(grok.Requests.Single().Model, Is.EqualTo("grok-4.5"));
            Assert.That(grok.Requests.Single().ApiEndpoint, Is.EqualTo("https://grok.example.invalid/v1"));
        });
    }

    [Test]
    public async Task SkipsWhenPolicySkipsPassiveGroupImage()
    {
        FakeImageRecognitionClient client = new("unused");
        QChatImageRecognitionService service = new(client);

        string? prompt = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            EnabledConfig(),
            Message("[CQ:image,file=cat.jpg,url=https://example.invalid/cat.jpg]", OneBotMessageType.Group),
            QChatSenderRole.GroupMember,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: true));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Is.Null);
            Assert.That(client.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task MissingPublicUrlCreatesFailurePromptWithoutCallingProvider()
    {
        FakeImageRecognitionClient client = new("unused");
        QChatImageRecognitionService service = new(client);

        string? prompt = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            EnabledConfig(),
            Message("[CQ:image,file=cat.jpg]", OneBotMessageType.Private),
            QChatSenderRole.Owner,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: false));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("image_1_status=failed"));
            Assert.That(prompt, Does.Contain("image_1_error=MissingPublicUrl"));
            Assert.That(client.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task PrivateImageUrlIsRejectedBeforeCallingVisionProvider()
    {
        FakeImageRecognitionClient client = new("should not be analyzed");
        QChatImageRecognitionService service = new(client);

        string? prompt = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            EnabledConfig(),
            Message("[CQ:image,file=private.jpg,url=https://127.0.0.1/private.jpg]", OneBotMessageType.Private),
            QChatSenderRole.Owner,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: false));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("image_1_status=failed"));
            Assert.That(prompt, Does.Contain("image_1_error=PolicySkipped"));
            Assert.That(prompt, Does.Contain("image_url_not_allowed"));
            Assert.That(prompt, Does.Not.Contain("127.0.0.1"));
            Assert.That(client.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ConfiguredImageHostAllowlistRejectsOtherPublicHosts()
    {
        FakeImageRecognitionClient client = new("should not be analyzed");
        QChatImageRecognitionService service = new(client);
        QChatConfig config = EnabledConfig();
        System.Reflection.PropertyInfo? allowedHosts = typeof(QChatConfig).GetProperty("ImageRecognitionAllowedImageHosts");
        Assert.That(allowedHosts, Is.Not.Null, "Vision image hosts must be configurable as an allowlist.");
        allowedHosts!.SetValue(config, "cdn.qq.com");

        string? prompt = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            config,
            Message("[CQ:image,file=public.jpg,url=https://example.invalid/public.jpg]", OneBotMessageType.Private),
            QChatSenderRole.Owner,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: false));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("image_url_not_allowed"));
            Assert.That(prompt, Does.Not.Contain("example.invalid"));
            Assert.That(client.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task BuildPromptAsync_LocalImageWithoutPublicUrlReportsUnavailable()
    {
        FakeImageRecognitionClient client = new("unused");
        QChatImageRecognitionService service = new(client);

        string? prompt = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            EnabledConfig(),
            Message("[CQ:image,file=local-only.image]", OneBotMessageType.Private),
            QChatSenderRole.Owner,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: false));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("public_url_unavailable"));
            Assert.That(client.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task SanitizesProviderNewlines()
    {
        FakeImageRecognitionClient client = new("line one\nline two");
        QChatImageRecognitionService service = new(client);

        string? prompt = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            EnabledConfig(),
            Message("[CQ:image,file=cat.jpg,url=https://example.invalid/cat.jpg]", OneBotMessageType.Private),
            QChatSenderRole.Owner,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: false));

        Assert.That(prompt, Does.Contain("image_1_summary=line one line two"));
    }

    [Test]
    public async Task WritesSafeUsageDiagnosticWithoutImageUrlSummaryOrCredentials()
    {
        List<(string EventName, string Detail, object? Data)> diagnostics = [];
        FakeImageRecognitionClient client = new(
            "summary should not be in diagnostics",
            new QChatImageRecognitionTokenUsage(1000, 30, 1030));
        QChatImageRecognitionService service = new(
            client,
            (eventName, detail, data, _) => diagnostics.Add((eventName, detail, data)));

        _ = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            EnabledConfig(),
            Message("[CQ:image,file=cat.jpg,url=https://example.invalid/cat.jpg]", OneBotMessageType.Private),
            QChatSenderRole.Owner,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: false));

        (string eventName, string detail, object? data) = diagnostics.Single();
        string diagnosticJson = JsonSerializer.Serialize(data);
        Assert.Multiple(() =>
        {
            Assert.That(eventName, Is.EqualTo("qchat-image-recognition-usage"));
            Assert.That(detail, Does.Contain("without image URLs"));
            Assert.That(diagnosticJson, Does.Contain("\"PromptTokens\":1000"));
            Assert.That(diagnosticJson, Does.Contain("\"CompletionTokens\":30"));
            Assert.That(diagnosticJson, Does.Contain("\"TotalTokens\":1030"));
            Assert.That(diagnosticJson, Does.Not.Contain("https://example.invalid/cat.jpg"));
            Assert.That(diagnosticJson, Does.Not.Contain("summary should not be in diagnostics"));
            Assert.That(diagnosticJson, Does.Not.Contain("Bearer"));
            Assert.That(diagnosticJson, Does.Not.Contain("Authorization"));
        });
    }

    [Test]
    public async Task UsageDiagnosticIncludesFailureKindsWithoutUnsafeDetails()
    {
        List<(string EventName, string Detail, object? Data)> diagnostics = [];
        FailingFakeImageRecognitionClient client = new(
            QChatImageRecognitionProviderResult.Fail(
                "agnes",
                "agnes-2.0-flash",
                QChatImageRecognitionFailureKind.HttpError,
                "http_403"));
        QChatImageRecognitionService service = new(
            client,
            (eventName, detail, data, _) => diagnostics.Add((eventName, detail, data)));

        _ = await service.BuildPromptAsync(new QChatImageRecognitionContext(
            EnabledConfig(),
            Message("[CQ:image,file=cat.jpg,url=https://example.invalid/cat.jpg]", OneBotMessageType.Private),
            QChatSenderRole.Owner,
            IsMentionedOrWoken: false,
            IsPassiveGroupMessage: false));

        string diagnosticJson = JsonSerializer.Serialize(diagnostics.Single().Data);
        Assert.Multiple(() =>
        {
            Assert.That(diagnosticJson, Does.Contain("\"FailureKinds\":[\"HttpError\"]"));
            Assert.That(diagnosticJson, Does.Not.Contain("http_403"));
            Assert.That(diagnosticJson, Does.Not.Contain("https://example.invalid/cat.jpg"));
            Assert.That(diagnosticJson, Does.Not.Contain("Bearer"));
            Assert.That(diagnosticJson, Does.Not.Contain("Authorization"));
        });
    }

    static QChatConfig EnabledConfig() => new()
    {
        EnableImageRecognition = true,
        AgnesVisionModel = "agnes-2.0-flash",
        ImageRecognitionMaxTokens = 80,
        MaxImagesPerMessage = 2,
        AnalyzeOwnerPrivateImages = true,
        AnalyzeOwnerGroupImages = true,
        AnalyzePrivateGuestImages = true,
        AnalyzeMentionedGroupImages = true,
        AnalyzePassiveGroupImages = false
    };

    static OneBotMessageEvent Message(string rawMessage, OneBotMessageType messageType) => new()
    {
        RawMessage = rawMessage,
        UserId = 1001,
        GroupId = messageType == OneBotMessageType.Group ? 2001 : 0
    };

    sealed class FakeImageRecognitionClient(
        string content,
        QChatImageRecognitionTokenUsage? usage = null) : IQChatImageRecognitionClient
    {
        public string ProviderName => "agnes";
        public int Calls { get; private set; }
        public List<QChatImageRecognitionProviderRequest> Requests { get; } = [];

        public Task<QChatImageRecognitionProviderResult> AnalyzeAsync(
            QChatImageRecognitionProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add(request);
            return Task.FromResult(QChatImageRecognitionProviderResult.Ok("agnes", request.Model, content, usage));
        }
    }

    sealed class FailingFakeImageRecognitionClient(
        QChatImageRecognitionProviderResult result) : IQChatImageRecognitionClient
    {
        public string ProviderName => "agnes";
        public int Calls { get; private set; }

        public Task<QChatImageRecognitionProviderResult> AnalyzeAsync(
            QChatImageRecognitionProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    sealed class NamedFakeImageRecognitionClient(string providerName, string content) : IQChatImageRecognitionClient
    {
        public string ProviderName { get; } = providerName;
        public int Calls { get; private set; }
        public List<QChatImageRecognitionProviderRequest> Requests { get; } = [];

        public Task<QChatImageRecognitionProviderResult> AnalyzeAsync(
            QChatImageRecognitionProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add(request);
            return Task.FromResult(QChatImageRecognitionProviderResult.Ok(ProviderName, request.Model, content));
        }
    }
}
