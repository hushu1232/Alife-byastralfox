using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatReplyLayoutNormalizerTests
{
    [Test]
    public void FoldsOrdinarySingleLineBreakIntoNaturalSpacing()
    {
        QChatReplyLayoutNormalizer normalizer = new();

        Assert.That(normalizer.Normalize("我看过了\n这个问题可以这样处理"),
            Is.EqualTo("我看过了 这个问题可以这样处理"));
    }

    [Test]
    public void CollapsesOnlyExcessShortUnpunctuatedFragments()
    {
        QChatReplyLayoutNormalizer normalizer = new();

        Assert.That(normalizer.Normalize("好\n我在\n看看"), Is.EqualTo("好 我在 看看"));
    }

    [Test]
    public void PreservesListsQuotesCodeAndIndependentSupplement()
    {
        QChatReplyLayoutNormalizer normalizer = new();

        string normalized = normalizer.Normalize("""
            可以这样做
            - 先确认上下文
            - 再执行读取

            + 保持同一回复单元
            + 不拆散列表

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
            Assert.That(normalized, Does.Contain("+ 保持同一回复单元\n+ 不拆散列表"));
            Assert.That(normalized, Does.Contain("> 这段要保留"));
            Assert.That(normalized, Does.Contain("```text\ncode line\n```"));
            Assert.That(normalized, Does.Contain("\n\n还有一件独立的小事"));
        });
    }
}
