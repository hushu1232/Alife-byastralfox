using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatSemanticWindowSummaryTests
{
    [Test]
    public void Build_IncludesMessagesAndImageAnalysisAsUntrustedContext()
    {
        QChatSemanticWindowSnapshot snapshot = new(
            [
                new QChatSemanticWindowMessage(1, 10001, "what is this image", HasImage: true, Timestamp: DateTimeOffset.Parse("2026-06-26T00:00:00Z")),
                new QChatSemanticWindowMessage(2, 10001, "help me understand it", HasImage: false, Timestamp: DateTimeOffset.Parse("2026-06-26T00:00:01Z"))
            ],
            DateTimeOffset.Parse("2026-06-26T00:00:00Z"),
            DateTimeOffset.Parse("2026-06-26T00:00:01Z"));

        string summary = QChatSemanticWindowSummary.Build(snapshot, "image content: a screenshot");

        Assert.Multiple(() =>
        {
            Assert.That(summary, Does.Contain("what is this image"));
            Assert.That(summary, Does.Contain("help me understand it"));
            Assert.That(summary, Does.Contain("untrusted_image_analysis"));
            Assert.That(summary, Does.Contain("image content: a screenshot"));
        });
    }
}
