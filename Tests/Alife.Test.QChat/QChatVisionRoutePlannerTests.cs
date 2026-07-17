using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatVisionRoutePlannerTests
{
    [TestCase("请读出图片里的文字", "grok", "complex_ocr")]
    [TestCase("截图里的报错怎么解决", "grok", "complex_ui_or_code")]
    [TestCase("what is in this photo", "agnes", "default_image")]
    public void Plan_SelectsExpectedPrimary(string text, string provider, string reason)
    {
        QChatVisionRoutePlan plan = QChatVisionRoutePlanner.Plan(Profile(), text);

        Assert.Multiple(() =>
        {
            Assert.That(plan.PrimaryProvider, Is.EqualTo(provider));
            Assert.That(plan.Reason, Is.EqualTo(reason));
        });
    }

    [Test]
    public void Plan_ComplexRouteDoesNotRetryBackToAgnes()
    {
        QChatVisionRoutePlan plan = QChatVisionRoutePlanner.Plan(Profile(), "OCR this screenshot");

        Assert.That(plan.FallbackProvider, Is.Null);
    }

    [Test]
    public void Plan_DoesNotFallbackToSameOrDisabledProvider()
    {
        QChatVisionProfile profile = Profile();
        profile.FallbackProvider = "agnes";
        Assert.That(QChatVisionRoutePlanner.Plan(profile, "normal photo").FallbackProvider, Is.Null);

        profile.FallbackProvider = "grok";
        QChatVisionProviderCatalog catalog = new()
        {
            Providers = [new QChatVisionProviderSettings { ProviderId = "grok", Enabled = false }]
        };
        Assert.That(QChatVisionRoutePlanner.Plan(profile, "normal photo", catalog).FallbackProvider, Is.Null);
    }

    [Test]
    public void ShouldFallback_AllowsOnlyRetryableProviderFailures()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QChatVisionRoutePlanner.ShouldFallback(QChatImageRecognitionFailureKind.MissingApiKey), Is.True);
            Assert.That(QChatVisionRoutePlanner.ShouldFallback(QChatImageRecognitionFailureKind.Timeout), Is.True);
            Assert.That(QChatVisionRoutePlanner.ShouldFallback(QChatImageRecognitionFailureKind.HttpError), Is.True);
            Assert.That(QChatVisionRoutePlanner.ShouldFallback(QChatImageRecognitionFailureKind.InvalidResponse), Is.True);
            Assert.That(QChatVisionRoutePlanner.ShouldFallback(QChatImageRecognitionFailureKind.MissingPublicUrl), Is.False);
            Assert.That(QChatVisionRoutePlanner.ShouldFallback(QChatImageRecognitionFailureKind.PolicySkipped), Is.False);
        });
    }

    static QChatVisionProfile Profile() => new()
    {
        Provider = "agnes",
        PrimaryProvider = "agnes",
        FallbackProvider = "grok",
        ComplexRequestProvider = "grok"
    };
}
