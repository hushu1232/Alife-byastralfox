using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public sealed record QChatFriendDeleteResult(bool Succeeded, string Message);

public interface IQChatFriendActionGateway
{
    Task<QChatFriendDeleteResult> DeleteFriendAsync(long userId, CancellationToken cancellationToken = default);
}

public sealed class QChatNoopFriendActionGateway : IQChatFriendActionGateway
{
    public Task<QChatFriendDeleteResult> DeleteFriendAsync(long userId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new QChatFriendDeleteResult(false, "friend_delete_gateway=not_enabled"));
    }
}
