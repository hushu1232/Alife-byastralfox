using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatCommandPersonaFormatterTests
{
    [Test]
    public void FormatForXiayuOwnerKeepsCommandDataAndAddsPersonaLead()
    {
        string formatted = QChatCommandPersonaFormatter.Format(
            "xiayu",
            QChatSenderRole.Owner,
            "file_policy=enabled\nread_blacklist_entries=12");

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.StartWith("术术，我看过了。"));
            Assert.That(formatted, Does.Contain("file_policy=enabled"));
            Assert.That(formatted, Does.Contain("read_blacklist_entries=12"));
            Assert.That(formatted.Length, Is.LessThan(260));
        });
    }

    [Test]
    public void FormatForDenialKeepsReasonClear()
    {
        string formatted = QChatCommandPersonaFormatter.Format(
            "xiayu",
            QChatSenderRole.PrivateGuest,
            "Only the owner can use desktop diagnostics.");

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("Only the owner can use desktop diagnostics."));
            Assert.That(formatted, Does.Not.Contain("file_policy=enabled"));
        });
    }

    [Test]
    public void FormatForMixuDoesNotUseXiayuOwnerAddress()
    {
        string formatted = QChatCommandPersonaFormatter.Format(
            "mixu",
            QChatSenderRole.Owner,
            "memory=connected");

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.StartWith("主人，状态在这里。"));
            Assert.That(formatted, Does.Contain("memory=connected"));
            Assert.That(formatted, Does.Not.Contain("术术"));
        });
    }

    [Test]
    public void FormatReturnsEmptyForEmptyInput()
    {
        Assert.That(QChatCommandPersonaFormatter.Format("xiayu", QChatSenderRole.Owner, "   "), Is.Empty);
    }
}
