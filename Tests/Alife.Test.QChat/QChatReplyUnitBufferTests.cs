using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatReplyUnitBufferTests
{
    [Test]
    public void Commit_FoldsOrdinaryLinesIntoOneVisibleReplyUnit()
    {
        QChatReplyUnitBuffer buffer = new();

        Assert.That(buffer.Commit("我看过了\n这个问题可以这样处理"),
            Is.EqualTo(new[] { "我看过了 这个问题可以这样处理" }));
    }

    [Test]
    public void Commit_SendsOneShortIndependentSupplementAfterThePrimaryReply()
    {
        QChatReplyUnitBuffer buffer = new();

        Assert.That(buffer.Commit("先把当前对话处理完。\n\n另外，日志我也会一起看。"),
            Is.EqualTo(new[] { "先把当前对话处理完。", "另外，日志我也会一起看。" }));
    }

    [Test]
    public void Commit_UsesTheSmallestWhitespaceChunksOnlyWhenTheHardLimitRequiresIt()
    {
        QChatReplyUnitBuffer buffer = new(7);

        Assert.That(buffer.Commit("one two three"), Is.EqualTo(new[] { "one two", "three" }));
    }

    [Test]
    public void Commit_RemovesInternalStateBeforeFoldingOrdinaryLines()
    {
        QChatReplyUnitBuffer buffer = new();

        Assert.That(buffer.Commit("正常\n状态: secret\n继续"), Is.EqualTo(new[] { "正常 继续" }));
    }
}
