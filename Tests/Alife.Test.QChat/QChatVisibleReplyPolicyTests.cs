using Alife.Function.QChat;
using NUnit.Framework;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Alife.Test.QChat;

public class QChatVisibleReplyPolicyTests
{
    const string PrivateOwnerLabel = "\u79C1\u804A\u4E3B\u4EBA";
    const string GroupReplyLabel = "\u7FA4\u91CC\u56DE\u590D";
    const string PsychologicalStateLabel = "\u5FC3\u7406\u72B6\u6001";
    const string InnerMonologueLabel = "\u5185\u5FC3\u72EC\u767D";
    const string StateLabel = "\u72B6\u6001";
    const string Dot = "\u3002";

    [Test]
    public void SelectsPrivateOwnerSectionWithoutSendingGroupSection()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            $"{PrivateOwnerLabel}\uFF1A\u672F\u672F\uFF0C\u6211\u5728\u3002\n\n{GroupReplyLabel}\uFF1A\u522B\u5435\u3002",
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo("\u672F\u672F\uFF0C\u6211\u5728\u3002"));
            Assert.That(result.Text, Does.Not.Contain(GroupReplyLabel));
        });
    }

    [Test]
    public void SelectsGroupSectionWithoutSendingPrivateOwnerSection()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            $"{PrivateOwnerLabel}\uFF1A\u6211\u7B49\u4E0B\u8DDF\u672F\u672F\u8BF4\u3002\n{GroupReplyLabel}\uFF1A\u77E5\u9053\u4E86\uFF0C\u522B\u5237\u5C4F\u3002",
            QChatConversationKind.Group,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo("\u77E5\u9053\u4E86\uFF0C\u522B\u5237\u5C4F\u3002"));
            Assert.That(result.Text, Does.Not.Contain(PrivateOwnerLabel));
        });
    }

    [Test]
    public void SelectsPrivateOwnerSectionWhenGroupSectionAppearsOnSameLine()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            $"{PrivateOwnerLabel}\uFF1A\u672F\u672F\uFF0C\u6211\u5728\u3002 {GroupReplyLabel}\uFF1A\u522B\u5435\u3002",
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo("\u672F\u672F\uFF0C\u6211\u5728\u3002"));
            Assert.That(result.Text, Does.Not.Contain(GroupReplyLabel));
            Assert.That(result.Text, Does.Not.Contain("\u522B\u5435"));
        });
    }

    [Test]
    public void SelectsGroupSectionWhenPrivateOwnerSectionAppearsOnSameLine()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            $"{PrivateOwnerLabel}\uFF1A\u672F\u672F\uFF0C\u6211\u5728\u3002 {GroupReplyLabel}\uFF1A\u522B\u5435\u3002",
            QChatConversationKind.Group,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo("\u522B\u5435\u3002"));
            Assert.That(result.Text, Does.Not.Contain(PrivateOwnerLabel));
            Assert.That(result.Text, Does.Not.Contain("\u672F\u672F"));
        });
    }

    [Test]
    public void EmbeddedQuotedGroupLabelIsNotTreatedAsSectionHeader()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            $"\u53EF\u4EE5\u8FD9\u6837\u5199\uFF1A{GroupReplyLabel}\uFF1A\u6536\u5230",
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo($"\u53EF\u4EE5\u8FD9\u6837\u5199\uFF1A{GroupReplyLabel}\uFF1A\u6536\u5230"));
        });
    }

    [Test]
    public void EmbeddedQuotedGroupLabelAfterProseWhitespaceIsNotTreatedAsSectionHeader()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            $"\u53EF\u4EE5\u8FD9\u6837\u5199\uFF1A {GroupReplyLabel}\uFF1A\u6536\u5230",
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo($"\u53EF\u4EE5\u8FD9\u6837\u5199\uFF1A {GroupReplyLabel}\uFF1A\u6536\u5230"));
        });
    }

    [Test]
    public void BlocksInternalStateAndUsesShortReactionWhenGroupShouldNotReply()
    {
        QChatVisibleReplyPolicy policy = new([Dot]);

        QChatVisibleReplyResult result = policy.Normalize(
            $"{PsychologicalStateLabel}\uFF1A\u4E0D\u60F3\u7406\u3002\n{InnerMonologueLabel}\uFF1A\u8FD9\u53E5\u4E0D\u80FD\u53D1\u3002",
            QChatConversationKind.Group,
            shouldReply: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo(Dot));
            Assert.That(result.Text, Does.Not.Contain(PsychologicalStateLabel));
            Assert.That(result.Text, Does.Not.Contain(InnerMonologueLabel));
        });
    }

    [TestCase("\u5C11\u72AF\u8D31\u3002")]
    [TestCase("\u6EDA\u8FDC\u70B9\u3002")]
    [TestCase("\u95ED\u5634\uFF0C\u5435\u5F97\u5F88\u3002")]
    public void AllowsAggressiveVisibleTextWithoutProfanityKeywordBlocking(string text)
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            text,
            QChatConversationKind.Group,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo(text));
        });
    }

    [Test]
    public void GroupNoReplyCanUseModelProvidedAggressiveColdReply()
    {
        QChatVisibleReplyPolicy policy = new([Dot]);

        QChatVisibleReplyResult result = policy.Normalize(
            "\u522B\u70E6\u3002",
            QChatConversationKind.Group,
            shouldReply: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo("\u522B\u70E6\u3002"));
            Assert.That(result.Reason, Does.Contain("model visible"));
        });
    }

    [Test]
    public void GroupNoReplyFallsBackToReactionWhenModelTextIsHiddenState()
    {
        QChatVisibleReplyPolicy policy = new([Dot]);

        QChatVisibleReplyResult result = policy.Normalize(
            "\uFF08\u4E0D\u56DE\u590D\uFF0C\u4FDD\u6301\u5B89\u9759\uFF09",
            QChatConversationKind.Group,
            shouldReply: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Is.EqualTo(Dot));
            Assert.That(result.Reason, Does.Contain("group no-reply reaction"));
        });
    }

    [Test]
    public void BlocksParenthesizedAggressiveStageDirectionEvenWhenItContainsProfanity()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            "\uFF08\u5C11\u72AF\u8D31\uFF0C\u61D2\u5F97\u56DE\u590D\uFF09",
            QChatConversationKind.Group,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }

    [Test]
    public void PrivateNoReplyInternalStateDoesNotSend()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            $"{StateLabel}\uFF1A\u5B89\u9759\u89C2\u5BDF\u3002",
            QChatConversationKind.Private,
            shouldReply: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }

    [Test]
    public void BlocksPersonaFrameRuntimeMarkersFromVisibleOutput()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            "[qchat persona frame]\nspeaker_role=non_owner\nrecommended_stance=hostile\n[/qchat persona frame]",
            QChatConversationKind.Group,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }

    [Test]
    public void BlocksPersonaFrameFieldMarkersFromVisibleOutput()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            "speaker_role=NonOwner\nrecommended_stance=HostilePushback",
            QChatConversationKind.Group,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }

    [Test]
    public void BlocksImageAnalysisRuntimeMarkersFromVisibleOutput()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            "[qchat image analysis]\nprovider=agnes\nimage_1_summary=cat\n[/qchat image analysis]",
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }

    [Test]
    public void BlocksImageAnalysisSensitiveFieldsFromVisibleOutput()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            "image_url=https://example.invalid/cat.jpg\nAuthorization: Bearer secret",
            QChatConversationKind.Group,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }

    [Test]
    public void InternalStateFilteringKeepsVisibleTechnicalStateAndProtocolText()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult statusCodeResult = policy.Normalize(
            "\u72B6\u6001\u7801 500 \u8868\u793A\u670D\u52A1\u7AEF\u9519\u8BEF",
            QChatConversationKind.Private,
            shouldReply: true);
        QChatVisibleReplyResult ospfResult = policy.Normalize(
            "OSPF \u914D\u7F6E\u9700\u8981\u5148\u786E\u8BA4\u90BB\u5C45",
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(statusCodeResult.ShouldSend, Is.True);
            Assert.That(statusCodeResult.Text, Is.EqualTo("\u72B6\u6001\u7801 500 \u8868\u793A\u670D\u52A1\u7AEF\u9519\u8BEF"));
            Assert.That(ospfResult.ShouldSend, Is.True);
            Assert.That(ospfResult.Text, Is.EqualTo("OSPF \u914D\u7F6E\u9700\u8981\u5148\u786E\u8BA4\u90BB\u5C45"));
        });
    }

    [Test]
    public void SharedPolicyInstanceCanSelectNoReplyReactionsConcurrently()
    {
        QChatVisibleReplyPolicy policy = new([Dot]);
        ConcurrentBag<string> reactions = [];

        Parallel.For(0, 100, _ =>
        {
            QChatVisibleReplyResult result = policy.Normalize(
                null,
                QChatConversationKind.Group,
                shouldReply: false);
            reactions.Add(result.Text);
        });

        Assert.Multiple(() =>
        {
            Assert.That(reactions, Has.Count.EqualTo(100));
            Assert.That(reactions, Is.All.EqualTo(Dot));
        });
    }

    [Test]
    public void AllowsAiTerminologyButBlocksSelfIdentification()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            "\u8FD9\u4E2A AI \u63A8\u7406\u95EE\u9898\u53EF\u4EE5\u62C6\u6210\u68C0\u7D22\u548C\u9A8C\u8BC1\u4E24\u6B65\uFF0C\u4F46\u6211\u4E0D\u662FAI\u3002",
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.True);
            Assert.That(result.Text, Does.Contain("AI \u63A8\u7406\u95EE\u9898"));
            Assert.That(result.Text, Does.Not.Contain("\u6211\u4E0D\u662FAI"));
        });
    }

    [Test]
    public void FullyUnsafePrivateTextDoesNotSend()
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            $"\u6211\u662FAI\u3002\n{PsychologicalStateLabel}\uFF1A\u4E0D\u5E94\u53D1\u9001\u3002",
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }

    [TestCase("[QQ Zone proactive] rejected")]
    [TestCase("qchat-send-failed")]
    [TestCase("/qchat xiayu state")]
    [TestCase("internal_action=qchat_get_self_state")]
    [TestCase("tool_call=qchat_get_self_state")]
    [TestCase("[XiaYu state - private, do not quote]\nmood=calm\n[/XiaYu state]")]
    [TestCase("route=qq:xiayu:private")]
    [TestCase("StopAfterTaskFeedback")]
    [TestCase("managed_file_id=abc123")]
    public void InternalRuntimeLabelsAreNotVisibleReplies(string text)
    {
        QChatVisibleReplyPolicy policy = new();

        QChatVisibleReplyResult result = policy.Normalize(
            text,
            QChatConversationKind.Private,
            shouldReply: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.ShouldSend, Is.False);
            Assert.That(result.Text, Is.Empty);
        });
    }
}
