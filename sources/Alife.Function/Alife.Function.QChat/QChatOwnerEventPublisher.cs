using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public interface IQChatOwnerEventPublisher
{
    Task<QChatOwnerEventEntry> PublishAsync(
        QChatOwnerEventRequest request,
        CancellationToken cancellationToken = default);

    Task<int> FlushAsync(CancellationToken cancellationToken = default);
    Task<int> FlushAsync(bool includeScheduled, CancellationToken cancellationToken = default);
    QChatOwnerEventSummary GetSummary();
    IReadOnlyList<QChatOwnerEventEntry> GetRecent(int maxCount);
}

public sealed class QChatOwnerEventPublisher(
    QChatOwnerEventOutbox outbox,
    QChatOwnerEventDispatcher dispatcher) : IQChatOwnerEventPublisher
{
    public async Task<QChatOwnerEventEntry> PublishAsync(
        QChatOwnerEventRequest request,
        CancellationToken cancellationToken = default)
    {
        QChatOwnerEventEntry entry = outbox.Enqueue(request);
        await dispatcher.FlushAsync(cancellationToken);
        return entry;
    }

    public Task<int> FlushAsync(CancellationToken cancellationToken = default) =>
        dispatcher.FlushAsync(cancellationToken);

    public Task<int> FlushAsync(bool includeScheduled, CancellationToken cancellationToken = default) =>
        dispatcher.FlushAsync(includeScheduled, cancellationToken);

    public QChatOwnerEventSummary GetSummary() => outbox.GetSummary();

    public IReadOnlyList<QChatOwnerEventEntry> GetRecent(int maxCount) => outbox.GetRecent(maxCount);
}
