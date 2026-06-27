using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public sealed class QChatOwnerEventDispatcher(
    QChatOwnerEventOutbox outbox,
    Func<IOneBotRuntime> runtimeProvider,
    int maxBatchSize = 20)
{
    readonly SemaphoreSlim flushGate = new(1, 1);

    public async Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        return await FlushAsync(includeScheduled: false, cancellationToken);
    }

    public async Task<int> FlushAsync(bool includeScheduled, CancellationToken cancellationToken = default)
    {
        if (includeScheduled)
        {
            await flushGate.WaitAsync(cancellationToken);
        }
        else if (!await flushGate.WaitAsync(0, cancellationToken))
        {
            return 0;
        }

        try
        {
            int delivered = 0;
            DateTimeOffset dueAt = includeScheduled ? DateTimeOffset.MaxValue : DateTimeOffset.Now;
            foreach (QChatOwnerEventEntry entry in outbox.GetPending(dueAt, maxBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    string formattedMessage = QChatCommandPersonaFormatter.Format(
                        entry.AgentId,
                        QChatSenderRole.Owner,
                        entry.Message);
                    OneBotSendMessageResult? result = await runtimeProvider()
                        .SendPrivateMessageWithResult(entry.OwnerId, formattedMessage);

                    outbox.MarkDelivered(entry.EventId, result?.MessageId, DateTimeOffset.Now);
                    delivered++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    outbox.MarkFailed(entry.EventId, ex.Message, DateTimeOffset.Now);
                }
            }

            return delivered;
        }
        finally
        {
            flushGate.Release();
        }
    }
}
