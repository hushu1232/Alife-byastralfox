using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatReplyLayoutNormalizerTests
{
    [Test]
    public void FoldsOrdinarySingleLineBreaksIntoOneNaturalParagraph()
    {
        QChatReplyLayoutNormalizer normalizer = new();

        Assert.That(normalizer.Normalize("我看过了\n这个问题可以这样处理\n先把入口收紧"),
            Is.EqualTo("我看过了 这个问题可以这样处理 先把入口收紧"));
    }

    [Test]
    public void PreservesListsQuotesCodeAndIndependentSupplement()
    {
        QChatReplyLayoutNormalizer normalizer = new();

        string normalized = normalizer.Normalize("""
            可以这样做
            - 先确认上下文
            - 再执行读取

            > 这段要保留

            ```text
            code line
            ```

            还有一件独立的小事
            """);

        Assert.Multiple(() =>
        {
            Assert.That(normalized, Does.Contain("可以这样做"));
            Assert.That(normalized, Does.Contain("- 先确认上下文\n- 再执行读取"));
            Assert.That(normalized, Does.Contain("> 这段要保留"));
            Assert.That(normalized, Does.Contain("```text\ncode line\n```"));
            Assert.That(normalized, Does.Contain("\n\n还有一件独立的小事"));
        });
    }
}
