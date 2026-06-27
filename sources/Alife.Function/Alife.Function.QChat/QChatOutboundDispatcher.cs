using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public sealed class QChatOutboundDispatcher
{
    public async Task DispatchAsync(
        QChatOutboundMessagePlan plan,
        Func<QChatOutboundMessageItem, CancellationToken, Task> sendAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sendAsync);

        ArgumentNullException.ThrowIfNull(plan.Items);
        IReadOnlyList<QChatOutboundMessageItem> items = plan.Items.ToArray();

        foreach (QChatOutboundMessageItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Kind == QChatOutboundItemKind.Text && string.IsNullOrWhiteSpace(item.Text))
                continue;

            await sendAsync(item, cancellationToken);
        }
    }
}
