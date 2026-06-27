using Alife.Function.Agent;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatWebResearchFormatterTests
{
    [Test]
    public void Format_GroupMemberSuccess_ReturnsShortConclusionAndTwoSources()
    {
        AgentWebResearchResult result = SuccessResult(4);
        QChatWebResearchFormatContext context = new(
            QChatSenderRole.GroupMember,
            OneBotMessageType.Group);

        string formatted = QChatWebResearchFormatter.Format(result, context);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.StartWith("\u7ed3\u8bba\uff1a"));
            Assert.That(formatted, Does.Contain("\u6d4b\u8bd5\u7ed3\u8bba"));
            Assert.That(CountSourceLines(formatted), Is.EqualTo(2));
            Assert.That(formatted, Does.Not.Contain("\u6765\u6e90 3"));
            Assert.That(formatted.Length, Is.LessThanOrEqualTo(420));
        });
    }

    [Test]
    public void Format_OwnerSuccess_AllowsThreeSources()
    {
        AgentWebResearchResult result = SuccessResult(4);
        QChatWebResearchFormatContext context = new(
            QChatSenderRole.Owner,
            OneBotMessageType.Group);

        string formatted = QChatWebResearchFormatter.Format(result, context);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.StartWith("\u7ed3\u8bba\uff1a"));
            Assert.That(CountSourceLines(formatted), Is.EqualTo(3));
            Assert.That(formatted, Does.Contain("\u6765\u6e90 3"));
            Assert.That(formatted, Does.Not.Contain("\u6765\u6e90 4"));
            Assert.That(formatted.Length, Is.LessThanOrEqualTo(760));
        });
    }

    [Test]
    public void Format_DefaultOverloadUsesConservativeLengthBound()
    {
        AgentWebResearchResult result = SuccessResult(4);

        string formatted = QChatWebResearchFormatter.Format(result);

        Assert.Multiple(() =>
        {
            Assert.That(CountSourceLines(formatted), Is.EqualTo(2));
            Assert.That(formatted.Length, Is.LessThanOrEqualTo(420));
        });
    }

    [TestCase("web_research_cooldown", "\u641c\u592a\u5feb\u4e86\uff0c\u7b49\u4e00\u4e0b\u3002")]
    [TestCase("web_research_busy", "\u73b0\u5728\u641c\u7d22\u961f\u5217\u6709\u70b9\u6ee1\uff0c\u7a0d\u540e\u518d\u8bd5\u3002")]
    [TestCase("empty_query", "\u4f60\u8981\u6211\u641c\u4ec0\u4e48\uff1f")]
    [TestCase("no_results", "\u6ca1\u67e5\u5230\u53ef\u9760\u6765\u6e90\u3002")]
    [TestCase("public_search_not_configured", "\u641c\u7d22\u73b0\u5728\u4e0d\u53ef\u7528\u3002")]
    [TestCase("other", "\u641c\u7d22\u5931\u8d25\uff0c\u5148\u4e0d\u4e71\u8bf4\u3002")]
    public void Format_Failure_ReturnsShortReadableMessage(string reason, string expected)
    {
        AgentWebResearchResult result = new(
            Success: false,
            Reason: reason,
            Query: "\u5929\u6c14",
            Answer: "",
            Evidence: []);

        string formatted = QChatWebResearchFormatter.Format(result);

        Assert.That(formatted, Is.EqualTo(expected));
    }

    [Test]
    public void Format_FailureFallback_ReturnsProvidedAnswer()
    {
        AgentWebResearchResult result = new(
            Success: false,
            Reason: "custom_error",
            Query: "x",
            Answer: "custom visible answer",
            Evidence: []);

        string formatted = QChatWebResearchFormatter.Format(result);

        Assert.That(formatted, Is.EqualTo("custom visible answer"));
    }

    [Test]
    public void Format_GroupMemberSuccess_TrimsLongSummariesWithinLengthBound()
    {
        AgentWebResearchResult result = new(
            Success: true,
            Reason: "ok",
            Query: "\u5f88\u957f\u7684\u95ee\u9898",
            Answer: "\u8fd9\u662f\u4e00\u4e2a\u5f88\u957f\u5f88\u957f\u7684\u7ed3\u8bba\u3002" + new string('x', 300),
            Evidence:
            [
                new AgentWebResearchEvidence("\u5f88\u957f\u7684\u6765\u6e90\u6807\u9898\u4e00" + new string('a', 160), "https://example.test/1", "\u5f88\u957f\u7684\u6458\u8981\u4e00" + new string('b', 260), "search"),
                new AgentWebResearchEvidence("\u5f88\u957f\u7684\u6765\u6e90\u6807\u9898\u4e8c" + new string('c', 160), "https://example.test/2", "\u5f88\u957f\u7684\u6458\u8981\u4e8c" + new string('d', 260), "search"),
                new AgentWebResearchEvidence("\u5f88\u957f\u7684\u6765\u6e90\u6807\u9898\u4e09", "https://example.test/3", "\u4e0d\u5e94\u8be5\u51fa\u73b0", "search"),
            ]);
        QChatWebResearchFormatContext context = new(
            QChatSenderRole.GroupMember,
            OneBotMessageType.Group);

        string formatted = QChatWebResearchFormatter.Format(result, context);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.StartWith("\u7ed3\u8bba\uff1a"));
            Assert.That(CountSourceLines(formatted), Is.EqualTo(2));
            Assert.That(formatted, Does.Not.Contain("\u4e0d\u5e94\u8be5\u51fa\u73b0"));
            Assert.That(formatted.Length, Is.LessThanOrEqualTo(420));
        });
    }

    static AgentWebResearchResult SuccessResult(int sourceCount)
    {
        List<AgentWebResearchEvidence> evidence = [];
        for (int i = 1; i <= sourceCount; i++)
        {
            evidence.Add(new AgentWebResearchEvidence(
                $"\u6765\u6e90 {i}",
                $"https://example.test/{i}",
                $"\u6458\u8981 {i}",
                "search"));
        }

        return new AgentWebResearchResult(
            Success: true,
            Reason: "ok",
            Query: "\u6d4b\u8bd5\u67e5\u8be2",
            Answer: "\u6d4b\u8bd5\u7ed3\u8bba",
            Evidence: evidence);
    }

    static int CountSourceLines(string formatted) =>
        formatted.Split('\n').Count(line => line.TrimStart().StartsWith("-", StringComparison.Ordinal));
}
