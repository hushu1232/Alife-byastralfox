using Alife.Function.LocalRuntime;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public enum LocalProductionNoticeKind { Draining, Recovery, RestartThreshold, CapabilityDegraded, CapabilityRecovered, BothAccountOutage }
public interface ILocalProductionNoticeSink { Task SendAsync(string text, CancellationToken cancellationToken); }
public sealed class QChatLocalProductionOwnerNotice(ILocalProductionNoticeSink sink)
{
    private string? lastNotice;
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task PublishAsync(LocalProductionNoticeKind kind, string accountId, SafeReasonCode reason, CancellationToken cancellationToken)
    {
        string safeAccount = accountId is "account-a" or "account-b" ? accountId : "unknown";
        string notice = $"local-production:{kind};account={safeAccount};reason={reason}";
        await gate.WaitAsync(cancellationToken); try { if (notice == lastNotice) return; lastNotice = notice; } finally { gate.Release(); }
        try { await sink.SendAsync(notice, cancellationToken); } catch { }
    }
}
