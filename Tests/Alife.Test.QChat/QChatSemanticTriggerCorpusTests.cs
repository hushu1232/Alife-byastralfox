using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatSemanticTriggerCorpusTests
{
    [TestCase("\u64a4\u4e86\u5427")]
    [TestCase("\u628a\u521a\u624d\u90a3\u6761\u64a4\u4e86")]
    [TestCase("\u5220\u6389\u4e0a\u4e00\u6761")]
    public void RecallCorpusConfirmsExecutionRequests(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyRecall(QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.RecallMessage));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetKind, Is.EqualTo(QChatIntentTargetKind.RecentBotMessage));
        });
    }

    [TestCase("\u4ed6\u662f\u4e0d\u662f\u4e0d\u4f1a\u64a4\u56de")]
    [TestCase("\u64a4\u56de\u529f\u80fd\u6d4b\u8bd5\u4e00\u4e0b")]
    [TestCase("\u4e0d\u8981\u64a4\u56de\uff0c\u6211\u53ea\u662f\u5728\u89e3\u91ca")]
    public void RecallCorpusRejectsMetaProbeAndNegation(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyRecall(QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.RecallMessage));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.TargetKind, Is.EqualTo(QChatIntentTargetKind.None));
        });
    }

    [TestCase("\u5148\u5b89\u9759\u4e00\u4e0b", "sleep")]
    [TestCase("\u522b\u8bf4\u8bdd\u4e86", "sleep")]
    [TestCase("\u9192\u9192", "wake")]
    [TestCase("\u53ef\u4ee5\u8bf4\u8bdd\u4e86", "wake")]
    public void QuietModeCorpusConfirmsDirectControlRequests(string text, string action)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyQuietMode(QChatIntentInput.FromText(text));

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
    public void QuietModeCorpusRejectsMetaAndProbePhrases(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyQuietMode(QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.QuietMode));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.IsMetaDiscussion, Is.True);
        });
    }

    [TestCase("\u4f60\u6df1\u591c\u60f3\u627e\u4eba\u8bf4\u8bdd\u7684\u65f6\u5019\u6211\u5c31\u5728\u8fd9\u91cc\uff1b\u4f60\u4e0d\u60f3\u8bf4\u7684\u65f6\u5019\uff0c\u6211\u4e5f\u53ef\u4ee5\u5b89\u9759\u5730\u966a\u7740\u4f60\u3002")]
    [TestCase("\u6211\u5931\u7720\u5176\u5b9e\u662f\u56e0\u4e3a\u5bb3\u6015\u7761\u89c9\uff0c\u4e0d\u662f\u8ba9\u4f60\u7761\u3002")]
    public void QuietModeCorpusRejectsSleepWordsInsidePastedProse(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyQuietMode(QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.QuietMode));
            Assert.That(decision.IsCandidate, Is.False);
            Assert.That(decision.IsConfirmed, Is.False);
        });
    }

    [TestCase("\u590f\u7fbd\uff0c\u51fa\u6765\u4e00\u4e0b")]
    [TestCase("\u5c0f\u7fbd\u5e2e\u6211\u770b\u770b")]
    [TestCase("\u590f\u7fbd\u5728\u5417\uff0c\u56de\u6211\u4e00\u4e0b")]
    public void GroupWakeCorpusConfirmsDirectedWakePhrases(string text)
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
        });
    }

    [TestCase("\u6211\u5728\u8ba8\u8bba\u590f\u7fbd\u8fd9\u4e2a\u540d\u5b57")]
    [TestCase("\u590f\u7fbd\u4f1a\u4e0d\u4f1a\u88ab\u5524\u9192")]
    [TestCase("\u4e0d\u662f\u5728\u53eb\u590f\u7fbd")]
    public void GroupWakeCorpusRejectsMetaAndNegatedMentions(string text)
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

    [Test]
    public void FileUploadCorpusConfirmsExplicitOwnerAuthoredGroupUploadRequest()
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyFileUpload(
            QChatIntentInput.FromText("\u628a D:\\tmp\\hello_world.c \u53d1\u5230\u5f53\u524d\u7fa4\u6587\u4ef6"));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.GroupFileUpload));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetKind, Is.EqualTo(QChatIntentTargetKind.CurrentSession));
            Assert.That(decision.FilePath, Is.EqualTo(@"D:\tmp\hello_world.c"));
        });
    }

    [Test]
    public void FileUploadCorpusRejectsImageMetadataOnlyCandidate()
    {
        QChatIntentInput input = new(
            PlainText: "",
            ReadableText: "\u56fe\u7247: fileid=abc",
            RawMessage: "[CQ:image,file=abc,fileid=abc]",
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
    public void AllowlistCorpusConfirmsCurrentGroupAdd()
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyAllowlist(
            QChatIntentInput.FromText("\u628a\u8fd9\u4e2a\u7fa4\u52a0\u5165\u767d\u540d\u5355"),
            currentGroupId: 1072509877);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.AllowlistUpdate));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetId, Is.EqualTo(1072509877));
            Assert.That(decision.TargetText, Is.EqualTo("group:add"));
        });
    }

    [Test]
    public void HelpMenuCorpusDoesNotTreatSentenceContainingCommandWordAsExactAlias()
    {
        Assert.That(QChatOwnerCommandService.IsHelpAliasCommand("\u6211\u53ea\u662f\u5728\u8ba8\u8bba\u6307\u4ee4\u8fd9\u4e24\u4e2a\u5b57"), Is.False);
    }
}
