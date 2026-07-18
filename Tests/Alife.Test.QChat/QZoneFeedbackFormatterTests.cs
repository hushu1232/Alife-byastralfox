using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QZoneFeedbackFormatterTests
{
    [TestCase(true, "published QQ Zone post", "已发布")]
    [TestCase(true, "commented on QQ Zone post", "已评论")]
    [TestCase(true, "replied to QQ Zone comment", "已回复")]
    [TestCase(true, "liked QQ Zone post", "已点赞")]
    [TestCase(true, "deleted QQ Zone post", "已删除")]
    [TestCase(false, "qzone_http_503", "接口暂时不可用")]
    [TestCase(false, "qzone_api_1001", "没有确认")]
    [TestCase(false, "qzone_session_unavailable", "登录状态不可用")]
    public void Format_MapsSafeReasonsToPersonaPrefixedChineseFeedback(bool succeeded, string safeReason, string expected)
    {
        string feedback = QZoneFeedbackFormatter.Format("mixu", succeeded, safeReason);

        Assert.Multiple(() =>
        {
            Assert.That(feedback, Does.StartWith("我已经整理好了。"));
            Assert.That(feedback, Does.Contain(expected));
            Assert.That(feedback, Does.Not.Contain(safeReason));
        });
    }

    [Test]
    public void Format_DoesNotExposeRawDiagnosticOrSecretBearingReason()
    {
        const string rawReason = "qzone_http_500 Cookie=p_skey=secret; BKN=123; https://qzone.qq.com/?token=secret; exception body";

        string feedback = QZoneFeedbackFormatter.Format("xiayu", false, rawReason);

        Assert.Multiple(() =>
        {
            Assert.That(feedback, Does.StartWith("状态如下。"));
            Assert.That(feedback, Does.Contain("接口暂时不可用"));
            Assert.That(feedback, Does.Not.Contain("Cookie"));
            Assert.That(feedback, Does.Not.Contain("BKN"));
            Assert.That(feedback, Does.Not.Contain("https://"));
            Assert.That(feedback, Does.Not.Contain("token"));
            Assert.That(feedback, Does.Not.Contain("exception body"));
        });
    }

    [Test]
    public void Format_UsesGenericSafeFeedbackForUnknownReasons()
    {
        const string rawReason = "unexpected error: Cookie=private-value";

        string feedback = QZoneFeedbackFormatter.Format(null, false, rawReason);

        Assert.Multiple(() =>
        {
            Assert.That(feedback, Does.StartWith("状态如下。"));
            Assert.That(feedback, Does.Contain("没有完成"));
            Assert.That(feedback, Does.Not.Contain(rawReason));
        });
    }
}
