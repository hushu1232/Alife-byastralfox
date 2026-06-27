using Alife.Function.Agent;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatBrowserAgentFormatterTests
{
    [Test]
    public void Format_Success_ReturnsCompactSourcedReply()
    {
        AgentBrowserAutomationResult result = new(
            true,
            "ok",
            "raw answer that should not be trusted as-is",
            [
                new AgentBrowserEvidence("Docs", "https://example.com/docs", "Install with dotnet tool install."),
                new AgentBrowserEvidence("Guide", "https://example.com/guide", "Configure the API key.")
            ],
            [],
            2);

        string text = QChatBrowserAgentFormatter.Format(result);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("Conclusion:"));
            Assert.That(text, Does.Contain("Docs"));
            Assert.That(text, Does.Contain("https://example.com/docs"));
            Assert.That(text.Length, Is.LessThanOrEqualTo(760));
        });
    }

    [Test]
    public void Format_LoginRequired_ReturnsShortBoundary()
    {
        AgentBrowserAutomationResult result = new(false, "browser_agent_login_required", "", [], [], 1);

        string text = QChatBrowserAgentFormatter.Format(result);

        Assert.That(text, Is.EqualTo("Cannot use that page because it requires login."));
    }

    [Test]
    public void Format_UnsafeUrl_ReturnsShortBoundary()
    {
        AgentBrowserAutomationResult result = new(false, "browser_agent_unsafe_url", "", [], [], 0);

        string text = QChatBrowserAgentFormatter.Format(result);

        Assert.That(text, Is.EqualTo("That browser target is not a safe public URL."));
    }

    [Test]
    public void FormatMediaOutputs_ImageResult_ReturnsQqImageSegment()
    {
        AgentBrowserMediaOutputResult[] outputs =
        [
            new(
                true,
                "ok",
                AgentBrowserMediaOutputKind.Image,
                "https://example.com/cat.png",
                @"D:\Alife\Runtime\BrowserAgentMedia\cat.png",
                @"D:\Alife\Runtime\BrowserAgentMedia\cat.png")
        ];

        IReadOnlyList<string> messages = QChatBrowserAgentFormatter.FormatMediaOutputs(outputs);

        Assert.Multiple(() =>
        {
            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("[CQ:image,file=D:/Alife/Runtime/BrowserAgentMedia/cat.png]"));
        });
    }

    [Test]
    public void FormatMediaOutputs_VideoResult_ReturnsTextLinkOnly()
    {
        AgentBrowserMediaOutputResult[] outputs =
        [
            new(
                true,
                "ok",
                AgentBrowserMediaOutputKind.VideoLink,
                "https://example.com/demo.mp4",
                "https://example.com/demo.mp4",
                null)
        ];

        IReadOnlyList<string> messages = QChatBrowserAgentFormatter.FormatMediaOutputs(outputs);

        Assert.Multiple(() =>
        {
            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("Video: https://example.com/demo.mp4"));
            Assert.That(messages[0], Does.Not.Contain("[CQ:"));
        });
    }
}
