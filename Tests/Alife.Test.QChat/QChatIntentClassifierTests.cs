using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatIntentClassifierTests
{
    [TestCase("撤了吧")]
    [TestCase("把那条撤了")]
    [TestCase("撤你刚才那句")]
    [TestCase("删掉刚才那条")]
    public void RecallIntentConfirmsNaturalOwnerCommands(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyRecall(
            QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.RecallMessage));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetKind, Is.EqualTo(QChatIntentTargetKind.RecentBotMessage));
            Assert.That(decision.HasNegation, Is.False);
            Assert.That(decision.IsMetaDiscussion, Is.False);
        });
    }

    [TestCase("他是不是不会撤回")]
    [TestCase("不要撤回，我只是解释")]
    [TestCase("为什么撤回失败")]
    [TestCase("能不能撤回")]
    public void RecallIntentRejectsMetaDiscussionAndNegation(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyRecall(
            QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.RecallMessage));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.HasNegation || decision.IsMetaDiscussion, Is.True);
        });
    }

    [TestCase("\u590f\u7fbd\uff0c\u56de\u6211\u4e00\u53e5\u64a4\u56de\u6d4b\u8bd5")]
    [TestCase("\u6211\u60f3\u6d4b\u8bd5\u4e00\u4e0b\u4f60\u4f1a\u4e0d\u4f1a\u64a4\u56de")]
    [TestCase("\u64a4\u56de\u529f\u80fd\u8bd5\u8bd5\u770b")]
    public void RecallIntentRejectsTestAndProbePhrasing(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyRecall(
            QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.RecallMessage));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.IsMetaDiscussion, Is.True);
        });
    }

    [Test]
    public void FileUploadIntentRejectsForwardImageMetadataFalsePositive()
    {
        QChatIntentInput input = new(
            PlainText: "",
            ReadableText: """
                          # 转发消息内容 (ID: 7653692629493460645)
                          ## 1094950020(QQ用户)：
                          [图片: https://multimedia.nt.qq.com.cn/download?appid=1407&fileid=abc]
                          ## 1094950020(QQ用户)：
                          输入群主就会出现这个
                          """,
            RawMessage: "[CQ:forward,id=7653692629493460645]",
            HasReply: false,
            ReplyMessageId: null);

        QChatIntentDecision decision = QChatIntentClassifier.ClassifyFileUpload(input);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.GroupFileUpload));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.Reason, Does.Contain("metadata"));
        });
    }

    [Test]
    public void AllowlistIntentParsesCurrentGroupAdd()
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyAllowlist(
            QChatIntentInput.FromText("把这个群加入白名单"),
            currentGroupId: 1072509877);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.AllowlistUpdate));
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetKind, Is.EqualTo(QChatIntentTargetKind.ExplicitGroup));
            Assert.That(decision.TargetId, Is.EqualTo(1072509877));
            Assert.That(decision.TargetText, Is.EqualTo("group:add"));
        });
    }

    [Test]
    public void AllowlistIntentParsesRawToolText()
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyAllowlist(
            QChatIntentInput.FromText("qchat_allowlist_update target=\"group\" action=\"add\" id=\"1072509877\""),
            currentGroupId: 0);

        Assert.Multiple(() =>
        {
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetId, Is.EqualTo(1072509877));
            Assert.That(decision.TargetText, Is.EqualTo("group:add"));
        });
    }

    [Test]
    public void AllowlistIntentRejectsForwardedHistoryFalsePositive()
    {
        QChatIntentInput input = new(
            PlainText: "",
            ReadableText: """
                          # 转发消息内容 (ID: forward-allowlist)
                          ## 3045846738(QQ用户):
                          把这个群加入白名单
                          """,
            RawMessage: "[CQ:forward,id=forward-allowlist]",
            HasReply: false,
            ReplyMessageId: null);

        QChatIntentDecision decision = QChatIntentClassifier.ClassifyAllowlist(input, currentGroupId: 1072509877);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.AllowlistUpdate));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.TargetId, Is.Null);
            Assert.That(decision.Reason, Does.Contain("forward"));
        });
    }

    [TestCase("\u5148\u5b89\u9759\u4e00\u4e0b", "sleep")]
    [TestCase("\u522b\u8bf4\u8bdd\u4e86", "sleep")]
    [TestCase("\u7fbd\uff0c\u5b89\u9759\u4e00\u70b9", "sleep")]
    [TestCase("\u7761\u4e00\u4f1a\u513f", "sleep")]
    [TestCase("\u9192\u9192", "wake")]
    [TestCase("\u7fbd\uff0c\u6062\u590d\u6b63\u5e38", "wake")]
    [TestCase("\u53ef\u4ee5\u8bf4\u8bdd\u4e86", "wake")]
    [TestCase("\u51fa\u6765\u5427", "wake")]
    public void QuietModeIntentConfirmsNaturalControlPhrases(string text, string action)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyQuietMode(
            QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.QuietMode));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetText, Is.EqualTo(action));
        });
    }

    [TestCase("\u5b89\u9759\u6a21\u5f0f\u662f\u4ec0\u4e48")]
    [TestCase("\u6d4b\u8bd5\u4e00\u4e0b\u80fd\u4e0d\u80fd\u5b89\u9759")]
    [TestCase("\u4f60\u4f1a\u4e0d\u4f1a\u88ab\u53eb\u9192")]
    public void QuietModeIntentRejectsMetaAndProbePhrases(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyQuietMode(
            QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.QuietMode));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.IsMetaDiscussion, Is.True);
        });
    }

    [TestCase("\u4f60\u53ef\u4ee5\u7d2f\uff0c\u53ef\u4ee5\u4e0d\u60f3\u8bf4\u8bdd\uff0c\u6211\u4e5f\u53ef\u4ee5\u5b89\u9759\u5730\u966a\u7740\u4f60\u3002")]
    [TestCase("\u6211\u5bb3\u6015\u7761\u89c9\u4e86\uff0c\u6240\u4ee5\u603b\u662f\u5931\u7720\u3002")]
    [TestCase("\u4ed6\u5728\u964c\u751f\u4eba\u9762\u524d\u4f1a\u5f88\u5b89\u9759\uff0c\u4f46\u8fd9\u53ea\u662f\u6027\u683c\u63cf\u8ff0\u3002")]
    public void QuietModeIntentDoesNotTreatEmbeddedProseAsSleepCommand(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyQuietMode(
            QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.QuietMode));
            Assert.That(decision.IsCandidate, Is.False);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.TargetText, Is.Null);
        });
    }

    [TestCase("\u590f\u7fbd\uff0c\u51fa\u6765\u4e00\u4e0b")]
    [TestCase("\u5c0f\u7fbd\u5e2e\u6211\u770b\u770b")]
    [TestCase("\u590f\u7fbd\u5728\u5417\uff0c\u56de\u6211\u4e00\u4e0b")]
    public void GroupWakeIntentConfirmsDirectedNaturalWakePhrases(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyGroupWake(
            QChatIntentInput.FromText(text),
            new[] { "\u590f\u7fbd", "\u7fbd", "\u5c0f\u7fbd" },
            isAtBot: false);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.GroupWake));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetKind, Is.EqualTo(QChatIntentTargetKind.CurrentSession));
        });
    }

    [TestCase("\u6211\u5728\u8ba8\u8bba\u590f\u7fbd\u8fd9\u4e2a\u540d\u5b57")]
    [TestCase("\u590f\u7fbd\u4f1a\u4e0d\u4f1a\u88ab\u5524\u9192")]
    [TestCase("\u4e0d\u662f\u5728\u53eb\u590f\u7fbd")]
    public void GroupWakeIntentRejectsMetaAndNegatedMentionPhrases(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyGroupWake(
            QChatIntentInput.FromText(text),
            new[] { "\u590f\u7fbd", "\u7fbd", "\u5c0f\u7fbd" },
            isAtBot: false);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.GroupWake));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.False);
        });
    }
}
