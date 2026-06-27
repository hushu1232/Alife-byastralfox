using System.Net;
using System.Net.Http;
using System.Text.Json;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatAgnesImageRecognitionClientTests
{
    [Test]
    public void ApiKeyResolverFallsBackToUserEnvironmentVariable()
    {
        string variableName = "ALIFE_TEST_AGNES_VISION_API_KEY_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variableName, null);
        Environment.SetEnvironmentVariable(variableName, "user-key", EnvironmentVariableTarget.User);

        try
        {
            string? resolved = QChatAgnesVisionApiKeyResolver.Resolve(
                configValue: "",
                environmentVariableName: variableName);

            Assert.That(resolved, Is.EqualTo("user-key"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Test]
    public async Task SendsOpenAiCompatibleImageUrlRequest()
    {
        RecordingHandler handler = new("""{"choices":[{"message":{"content":"cat on desk"}}],"usage":{"prompt_tokens":123,"completion_tokens":7,"total_tokens":130}}""");
        QChatAgnesImageRecognitionClient client = new(
            new HttpClient(handler),
            () => "test-key",
            "https://apihub.agnes-ai.com/v1/chat/completions");

        QChatImageRecognitionProviderResult result = await client.AnalyzeAsync(new QChatImageRecognitionProviderRequest(
            "https://example.invalid/cat.jpg",
            "Describe this image.",
            "agnes-2.0-flash",
            80));

        using JsonDocument requestJson = JsonDocument.Parse(handler.RequestBody);
        JsonElement root = requestJson.RootElement;
        JsonElement userContent = root.GetProperty("messages")[1].GetProperty("content");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Content, Is.EqualTo("cat on desk"));
            Assert.That(result.Usage?.PromptTokens, Is.EqualTo(123));
            Assert.That(result.Usage?.CompletionTokens, Is.EqualTo(7));
            Assert.That(result.Usage?.TotalTokens, Is.EqualTo(130));
            Assert.That(handler.RequestUri?.ToString(), Is.EqualTo("https://apihub.agnes-ai.com/v1/chat/completions"));
            Assert.That(handler.Authorization, Is.EqualTo("Bearer test-key"));
            Assert.That(root.GetProperty("model").GetString(), Is.EqualTo("agnes-2.0-flash"));
            Assert.That(userContent[0].GetProperty("type").GetString(), Is.EqualTo("text"));
            Assert.That(userContent[1].GetProperty("type").GetString(), Is.EqualTo("image_url"));
            Assert.That(userContent[1].GetProperty("image_url").GetProperty("url").GetString(), Is.EqualTo("https://example.invalid/cat.jpg"));
        });
    }

    [Test]
    public async Task MissingApiKeyDoesNotSendRequest()
    {
        RecordingHandler handler = new("""{"choices":[{"message":{"content":"unused"}}]}""");
        QChatAgnesImageRecognitionClient client = new(new HttpClient(handler), () => "", "https://apihub.agnes-ai.com/v1/chat/completions");

        QChatImageRecognitionProviderResult result = await client.AnalyzeAsync(new QChatImageRecognitionProviderRequest(
            "https://example.invalid/cat.jpg",
            "Describe this image.",
            "agnes-2.0-flash",
            80));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(QChatImageRecognitionFailureKind.MissingApiKey));
            Assert.That(handler.CallCount, Is.Zero);
        });
    }

    [Test]
    public async Task HttpErrorDoesNotLeakBodyOrKeyInFailureReason()
    {
        RecordingHandler handler = new("secret server details", HttpStatusCode.BadGateway);
        QChatAgnesImageRecognitionClient client = new(new HttpClient(handler), () => "secret-key", "https://apihub.agnes-ai.com/v1/chat/completions");

        QChatImageRecognitionProviderResult result = await client.AnalyzeAsync(new QChatImageRecognitionProviderRequest(
            "https://example.invalid/cat.jpg",
            "Describe this image.",
            "agnes-2.0-flash",
            80));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureKind, Is.EqualTo(QChatImageRecognitionFailureKind.HttpError));
            Assert.That(result.FailureReason, Does.Not.Contain("secret"));
            Assert.That(result.FailureReason, Is.EqualTo("http_502"));
        });
    }

    [Test]
    public async Task InvalidResponseReturnsInvalidResponseFailure()
    {
        RecordingHandler handler = new("""{"choices":[]}""");
        QChatAgnesImageRecognitionClient client = new(new HttpClient(handler), () => "test-key", "https://apihub.agnes-ai.com/v1/chat/completions");

        QChatImageRecognitionProviderResult result = await client.AnalyzeAsync(new QChatImageRecognitionProviderRequest(
            "https://example.invalid/cat.jpg",
            "Describe this image.",
            "agnes-2.0-flash",
            80));

        Assert.That(result.FailureKind, Is.EqualTo(QChatImageRecognitionFailureKind.InvalidResponse));
    }

    sealed class RecordingHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = request.Content == null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody)
            };
        }
    }
}
