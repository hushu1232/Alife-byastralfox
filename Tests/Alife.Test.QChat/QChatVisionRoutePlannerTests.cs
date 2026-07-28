using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatVisionRoutePlannerTests
{
    [TestCase("请读出图片里的文字", "grok", "complex_ocr")]
    [TestCase("把这张发票的文字提取出来", "grok", "complex_ocr")]
    [TestCase("截图里的报错怎么解决", "grok", "complex_ui_or_code")]
    [TestCase("这段文字写得很好", "agnes", "default_image")]
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
    public void Plan_OcrGetsLongerTimeoutWithoutSlowingNormalImages()
    {
        TimeSpan requested = TimeSpan.FromSeconds(12);

        QChatVisionRoutePlan ocr = QChatVisionRoutePlanner.Plan(Profile(), "请识别图片里的文字", totalTimeout: requested);
        QChatVisionRoutePlan normal = QChatVisionRoutePlanner.Plan(Profile(), "普通照片", totalTimeout: requested);

        Assert.Multiple(() =>
        {
            Assert.That(ocr.TotalTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(normal.TotalTimeout, Is.EqualTo(requested));
        });
    }

    [Test]
    public void Plan_UsesProviderSpecificTimeouts()
    {
        QChatVisionProviderCatalog catalog = new()
        {
            Providers =
            [
                new QChatVisionProviderSettings { ProviderId = "agnes", TimeoutMilliseconds = 12000 },
                new QChatVisionProviderSettings { ProviderId = "grok", TimeoutMilliseconds = 90000 }
            ]
        };

        QChatVisionRoutePlan normal = QChatVisionRoutePlanner.Plan(
            Profile(), "普通照片", catalog, TimeSpan.FromSeconds(12));
        QChatVisionRoutePlan complex = QChatVisionRoutePlanner.Plan(
            Profile(), "请识别图片里的文字", catalog, TimeSpan.FromSeconds(12));

        Assert.Multiple(() =>
        {
            Assert.That(normal.TotalTimeout, Is.EqualTo(TimeSpan.FromSeconds(12)));
            Assert.That(normal.FallbackTimeout, Is.EqualTo(TimeSpan.FromSeconds(90)));
            Assert.That(complex.TotalTimeout, Is.EqualTo(TimeSpan.FromSeconds(90)));
            Assert.That(complex.FallbackTimeout, Is.Null);
        });
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
