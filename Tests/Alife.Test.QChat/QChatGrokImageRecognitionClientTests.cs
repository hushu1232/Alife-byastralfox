using System.Net;
using System.Net.Http;
using System.Text.Json;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatGrokImageRecognitionClientTests
{
    [Test]
    public void VisionApiKeyResolverUsesOnlyItsOwnEnvironmentVariable()
    {
        string variableName = "ALIFE_TEST_GROK_VISION_API_KEY_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variableName, "vision-key");

        try
        {
            Assert.That(QChatGrokVisionApiKeyResolver.Resolve(variableName), Is.EqualTo("vision-key"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Test]
    public async Task SendsOpenAiCompatibleImageUrlRequest()
    {
        RecordingHandler handler = new("""{"choices":[{"message":{"content":"screenshot text"}}],"usage":{"prompt_tokens":123,"completion_tokens":7,"total_tokens":130}}""");
        QChatGrokImageRecognitionClient client = new(
            new HttpClient(handler),
            () => "test-key",
            "https://vision.example.invalid/v1/chat/completions");

        QChatImageRecognitionProviderResult result = await client.AnalyzeAsync(new QChatImageRecognitionProviderRequest(
            "https://example.invalid/screenshot.jpg",
            "Read the screenshot.",
            "grok-4.5",
            80));

        using JsonDocument requestJson = JsonDocument.Parse(handler.RequestBody);
        JsonElement userContent = requestJson.RootElement.GetProperty("messages")[1].GetProperty("content");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.ProviderName, Is.EqualTo("grok"));
            Assert.That(result.Content, Is.EqualTo("screenshot text"));
            Assert.That(handler.Authorization, Is.EqualTo("Bearer test-key"));
            Assert.That(userContent[1].GetProperty("type").GetString(), Is.EqualTo("image_url"));
            Assert.That(userContent[1].GetProperty("image_url").GetProperty("url").GetString(), Is.EqualTo("https://example.invalid/screenshot.jpg"));
        });
    }

    [Test]
    public async Task HttpErrorDoesNotLeakProviderBodyOrKey()
    {
        RecordingHandler handler = new("provider-response-must-stay-private", HttpStatusCode.BadGateway);
        QChatGrokImageRecognitionClient client = new(
            new HttpClient(handler),
            () => "test-key-not-for-output",
            "https://vision.example.invalid/v1/chat/completions");

        QChatImageRecognitionProviderResult result = await client.AnalyzeAsync(new QChatImageRecognitionProviderRequest(
            "https://example.invalid/screenshot.jpg", "Read it.", "grok-4.5", 80));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(QChatImageRecognitionFailureKind.HttpError));
            Assert.That(result.FailureReason, Is.EqualTo("http_502"));
            Assert.That(result.FailureReason, Does.Not.Contain("provider-response"));
            Assert.That(result.FailureReason, Does.Not.Contain("test-key"));
        });
    }

    [Test]
    public async Task InvalidEndpointFailsWithoutSendingRequest()
    {
        RecordingHandler handler = new("unused");
        QChatGrokImageRecognitionClient client = new(new HttpClient(handler), () => "test-key", "not a valid endpoint");

        QChatImageRecognitionProviderResult result = await client.AnalyzeAsync(new QChatImageRecognitionProviderRequest(
            "https://example.invalid/screenshot.jpg", "Read it.", "grok-4.5", 80));

        Assert.Multiple(() =>
        {
            Assert.That(result.FailureKind, Is.EqualTo(QChatImageRecognitionFailureKind.InvalidResponse));
            Assert.That(result.FailureReason, Is.EqualTo("invalid_endpoint"));
            Assert.That(handler.CallCount, Is.Zero);
        });
    }

    sealed class RecordingHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? Authorization { get; private set; }
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode) { Content = new StringContent(responseBody) };
        }
    }
}
