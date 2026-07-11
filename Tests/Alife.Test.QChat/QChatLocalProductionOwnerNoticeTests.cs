using Alife.Function.LocalRuntime;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatLocalProductionOwnerNoticeTests
{
    [Test]
    public async Task Duplicate_notice_is_sent_once_and_send_failure_does_not_escape()
    {
        FakeSink sink = new(); QChatLocalProductionOwnerNotice notice = new(sink);
        await notice.PublishAsync(LocalProductionNoticeKind.Draining, "account-a", SafeReasonCode.Busy, CancellationToken.None);
        await notice.PublishAsync(LocalProductionNoticeKind.Draining, "account-a", SafeReasonCode.Busy, CancellationToken.None);
        Assert.That(sink.Calls, Is.EqualTo(1));
        Assert.DoesNotThrowAsync(() => new QChatLocalProductionOwnerNotice(new ThrowingSink()).PublishAsync(LocalProductionNoticeKind.BothAccountOutage, "account-a", SafeReasonCode.DependencyUnavailable, CancellationToken.None));
    }
    private sealed class FakeSink : ILocalProductionNoticeSink { public int Calls { get; private set; } public Task SendAsync(string text, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; } }
    private sealed class ThrowingSink : ILocalProductionNoticeSink { public Task SendAsync(string text, CancellationToken cancellationToken) => throw new InvalidOperationException(); }
}
