using Alife.Function.QChat;
using NUnit.Framework;

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
}
