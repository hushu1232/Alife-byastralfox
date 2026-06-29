using System.IO;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatToolRouteStateWiringTests
{
    [Test]
    public void DispatchToModelCreatesScopedToolRouteState()
    {
        string source = File.ReadAllText(FindQChatServiceSource());

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("protected virtual async Task<string> DispatchToModelAsync"));
            Assert.That(source, Does.Contain("functionService.CreateToolRouteState"));
            Assert.That(source, Does.Contain("message.SenderRole == QChatSenderRole.Owner"));
            Assert.That(source, Does.Contain("message.MessageType == OneBotMessageType.Private"));
            Assert.That(source, Does.Contain("functionService.UseToolRouteState(routeState)"));
            Assert.That(source, Does.Contain("return await ChatBot.ChatAsync(ChatTextFilter(message.Formatted))"));
        });
    }

    static string FindQChatServiceSource()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            string path = Path.Combine(
                directory.FullName,
                "sources",
                "Alife.Function",
                "Alife.Function.QChat",
                "QChatService.cs");
            if (File.Exists(path))
                return path;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate QChatService.cs from the test output directory.");
    }
}

