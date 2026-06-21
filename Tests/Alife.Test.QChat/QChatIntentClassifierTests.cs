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
}
