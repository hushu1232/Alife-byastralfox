using Alife.Function.QChat;
using NUnit.Framework;
using System.Text.Json;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatVisionReadinessTests
{
    [Test]
    public void Evaluate_DisabledConfigReportsDisabled()
    {
        QChatVisionReadinessStatus status = QChatVisionReadiness.Evaluate(
            new QChatConfig
            {
                EnableImageRecognition = false,
                ImageRecognitionProvider = "agnes",
                AgnesVisionModel = "agnes-2.0-flash",
                MaxImagesPerMessage = 2
            },
            () => "key");

        Assert.Multiple(() =>
        {
            Assert.That(status.Ready, Is.False);
            Assert.That(status.Status, Is.EqualTo("disabled"));
            Assert.That(status.Reason, Is.EqualTo("image_recognition_disabled"));
            Assert.That(status.ApiKeyConfigured, Is.True);
        });
    }

    [Test]
    public void Evaluate_EnabledWithoutApiKeyReportsMissingApiKey()
    {
        QChatVisionReadinessStatus status = QChatVisionReadiness.Evaluate(
            new QChatConfig
            {
                EnableImageRecognition = true,
                ImageRecognitionProvider = "agnes",
                AgnesVisionModel = "agnes-2.0-flash"
            },
            () => "");

        Assert.Multiple(() =>
        {
            Assert.That(status.Ready, Is.False);
            Assert.That(status.Status, Is.EqualTo("missing_api_key"));
            Assert.That(status.Reason, Is.EqualTo("agnes_api_key_missing"));
            Assert.That(status.ApiKeyConfigured, Is.False);
        });
    }

    [Test]
    public void Evaluate_EnabledWithApiKeyReportsReady()
    {
        QChatVisionReadinessStatus status = QChatVisionReadiness.Evaluate(
            new QChatConfig
            {
                EnableImageRecognition = true,
                ImageRecognitionProvider = "agnes",
                AgnesVisionModel = "agnes-2.0-flash",
                AgnesVisionApiEndpoint = "https://apihub.agnes-ai.com/v1/chat/completions",
                MaxImagesPerMessage = 2
            },
            () => "key");

        Assert.Multiple(() =>
        {
            Assert.That(status.Ready, Is.True);
            Assert.That(status.Status, Is.EqualTo("ready"));
            Assert.That(status.Reason, Is.EqualTo("ready"));
            Assert.That(status.Provider, Is.EqualTo("agnes"));
            Assert.That(status.Model, Is.EqualTo("agnes-2.0-flash"));
            Assert.That(status.PublicUrlRequired, Is.True);
            Assert.That(status.ApiKeyConfigured, Is.True);
            Assert.That(status.MaxImagesPerMessage, Is.EqualTo(2));
        });
    }

    [Test]
    public void Evaluate_ProfileReportsPrimaryAndFallbackWithoutCredentialValues()
    {
        QChatConfig config = new()
        {
            EnableImageRecognition = true,
            VisionProviders = new QChatVisionProviderCatalog
            {
                Providers =
                [
                    new QChatVisionProviderSettings { ProviderId = "agnes", Model = "agnes-2.0-flash", ApiEndpoint = "https://agnes.example.invalid/v1" },
                    new QChatVisionProviderSettings { ProviderId = "grok", Model = "grok-4.5", ApiEndpoint = "https://grok.example.invalid/v1" }
                ]
            }
        };
        QChatVisionProfile profile = new()
        {
            PrimaryProvider = "agnes",
            FallbackProvider = "grok",
            ComplexRequestProvider = "grok"
        };
        QChatVisionReadinessStatus status = QChatVisionReadiness.Evaluate(
            config,
            profile,
            new Dictionary<string, Func<string?>>
            {
                ["agnes"] = () => "test-key-a",
                ["grok"] = () => "test-key-g"
            });

        string json = JsonSerializer.Serialize(status);
        Assert.Multiple(() =>
        {
            Assert.That(status.Ready, Is.True);
            Assert.That(status.Provider, Is.EqualTo("agnes"));
            Assert.That(status.FallbackProvider, Is.EqualTo("grok"));
            Assert.That(status.FallbackApiKeyConfigured, Is.True);
            Assert.That(json, Does.Not.Contain("test-key-a"));
            Assert.That(json, Does.Not.Contain("test-key-g"));
        });
    }
}
