using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public sealed record QChatFriendDeleteResult(bool Succeeded, string Message);

public sealed record QChatFriendActionGatewayOptions
{
    public string DeleteFriendAction { get; init; } = "delete_friend";
    public bool TempBlock { get; init; }
    public bool TempBothDelete { get; init; }
}

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

public sealed class QChatOneBotFriendActionGateway(
    IOneBotActionInvoker invoker,
    QChatFriendActionGatewayOptions? options = null) : IQChatFriendActionGateway
{
    readonly QChatFriendActionGatewayOptions options = options ?? new QChatFriendActionGatewayOptions();

    public QChatOneBotFriendActionGateway(IOneBotRuntime runtime, QChatFriendActionGatewayOptions? options = null)
        : this(new OneBotRuntimeActionInvoker(runtime), options)
    {
    }

    public async Task<QChatFriendDeleteResult> DeleteFriendAsync(long userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await invoker.CallActionAsync<object>(options.DeleteFriendAction, new {
                user_id = userId,
                temp_block = options.TempBlock,
                temp_both_del = options.TempBothDelete
            });
            return new QChatFriendDeleteResult(true, $"friend_delete_action={options.DeleteFriendAction}");
        }
        catch (Exception ex)
        {
            return new QChatFriendDeleteResult(false, $"friend_delete_action={options.DeleteFriendAction} error={ex.Message}");
        }
    }

    sealed class OneBotRuntimeActionInvoker(IOneBotRuntime runtime) : IOneBotActionInvoker
    {
        public Task<T?> CallActionAsync<T>(string action, object? parameters = null)
        {
            return runtime.CallActionAsync<T>(action, parameters);
        }
    }
}
