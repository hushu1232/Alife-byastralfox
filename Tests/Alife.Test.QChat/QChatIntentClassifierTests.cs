using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatIntentClassifierTests
{
    const string XiayuSleepCommand = "Night has fallen";
    const string XiayuWakeCommand = "wake up ， show me the flower";
    const string MixuSleepCommand = "晚安咪绪";
    const string MixuWakeCommand = "耄耋快起床";

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

    [TestCase(XiayuSleepCommand, XiayuSleepCommand, XiayuWakeCommand, "sleep")]
    [TestCase("N i g h t\u2003H A S\u00a0F A L L E N", XiayuSleepCommand, XiayuWakeCommand, "sleep")]
    [TestCase(XiayuWakeCommand, XiayuSleepCommand, XiayuWakeCommand, "wake")]
    [TestCase("W A K E U P ， S H O W M E T H E F L O W E R", XiayuSleepCommand, XiayuWakeCommand, "wake")]
    [TestCase(MixuSleepCommand, MixuSleepCommand, MixuWakeCommand, "sleep")]
    [TestCase("晚 安 咪 绪", MixuSleepCommand, MixuWakeCommand, "sleep")]
    [TestCase(MixuWakeCommand, MixuSleepCommand, MixuWakeCommand, "wake")]
    [TestCase("耄 耋 快 起 床", MixuSleepCommand, MixuWakeCommand, "wake")]
    public void QuietModeIntentConfirmsOnlyConfiguredWholeMessage(
        string text,
        string sleepCommand,
        string wakeCommand,
        string action)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyQuietMode(
            QChatIntentInput.FromText(text),
            sleepCommand,
            wakeCommand);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.QuietMode));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetText, Is.EqualTo(action));
        });
    }

    [TestCase("我说 Night has fallen 只是歌词")]
    [TestCase("Night has fallen。")]
    [TestCase("wake up, show me the flower")]
    [TestCase("醒醒")]
    [TestCase("回来")]
    [TestCase("我等会回来")]
    [TestCase("你去睡觉吧")]
    public void QuietModeIntentRejectsAnythingOtherThanConfiguredWholeMessage(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyQuietMode(
            QChatIntentInput.FromText(text),
            XiayuSleepCommand,
            XiayuWakeCommand);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.QuietMode));
            Assert.That(decision.IsCandidate, Is.False);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.TargetText, Is.Null);
        });
    }

    [Test]
    public void QuietModeIntentDoesNotScanReadableOrForwardedContent()
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyQuietMode(
            new QChatIntentInput(
                PlainText: "看看这条转发",
                ReadableText: XiayuSleepCommand,
                RawMessage: "[CQ:forward,id=quiet-command]",
                HasReply: false,
                ReplyMessageId: null),
            XiayuSleepCommand,
            XiayuWakeCommand);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.QuietMode));
            Assert.That(decision.IsCandidate, Is.False);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.TargetText, Is.Null);
        });
    }

    [Test]
    public void QuietModeIntentRejectsAmbiguousConfiguration()
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyQuietMode(
            QChatIntentInput.FromText(XiayuSleepCommand),
            XiayuSleepCommand,
            XiayuSleepCommand);

        Assert.Multiple(() =>
        {
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.TargetText, Is.Null);
            Assert.That(decision.Reason, Does.Contain("ambiguous"));
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
