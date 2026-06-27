using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Alife.Function.QChat;

public interface IOneBotActionInvoker
{
    Task<T?> CallActionAsync<T>(string action, object? parameters = null);
}

public interface IOneBotActionConnection : IOneBotActionInvoker, IAsyncDisposable
{
    bool IsConnected { get; }
    string Url { get; set; }
    string Token { get; set; }
    Task ConnectAsync();
}

public sealed class OneBotClientActionInvoker(OneBotClient client) : IOneBotActionInvoker
{
    public Task<T?> CallActionAsync<T>(string action, object? parameters = null)
    {
        return client.CallActionAsync<T>(action, parameters);
    }
}

public sealed class OneBotClientActionConnection(OneBotClient client) : IOneBotActionConnection
{
    public bool IsConnected => client.IsConnected;
    public string Url { get => client.Url; set => client.Url = value; }
    public string Token { get => client.Token; set => client.Token = value; }
    public Task ConnectAsync() => client.ConnectAsync();
    public Task<T?> CallActionAsync<T>(string action, object? parameters = null) => client.CallActionAsync<T>(action, parameters);
    public ValueTask DisposeAsync() => client.DisposeAsync();
}

public sealed record OneBotQZoneRuntimeOptions
{
    public string PostAction { get; init; } = "send_msg";
    public string CommentAction { get; init; } = "send_comment";
    public string LikeAction { get; init; } = "send_like";
    public string LatestPostAction { get; init; } = "get_qzone_latest_post";
    public string LatestCommentsAction { get; init; } = "get_qzone_comments";
}

public sealed class OneBotQZoneRuntime(
    IOneBotActionInvoker invoker,
    OneBotQZoneRuntimeOptions? options = null) : IQZoneRuntime
{
    readonly OneBotQZoneRuntimeOptions options = options ?? new OneBotQZoneRuntimeOptions();

    public Task PublishPost(string content)
    {
        return invoker.CallActionAsync<object>(options.PostAction, new {
            message = content
        });
    }

    public Task Comment(long targetId, string postId, string content)
    {
        return invoker.CallActionAsync<object>(options.CommentAction, new {
            target_uin = targetId,
            target_tid = postId,
            content
        });
    }

    public Task ReplyComment(long targetId, string postId, string commentId, string content)
    {
        return invoker.CallActionAsync<object>(options.CommentAction, new {
            target_uin = targetId,
            target_tid = postId,
            comment_id = commentId,
            content
        });
    }

    public Task LikePost(long targetId, string postId)
    {
        return invoker.CallActionAsync<object>(options.LikeAction, new {
            target_uin = targetId,
            target_tid = postId
        });
    }

    public Task<QZonePostSnapshot?> GetLatestPost(long targetId)
    {
        return invoker.CallActionAsync<QZonePostSnapshot>(options.LatestPostAction, new {
            target_uin = targetId
        });
    }

    public async Task<IReadOnlyList<QZoneCommentSnapshot>> GetLatestComments(long targetId, string postId, int count)
    {
        List<QZoneCommentSnapshot>? comments = await invoker.CallActionAsync<List<QZoneCommentSnapshot>>(options.LatestCommentsAction, new {
            target_uin = targetId,
            target_tid = postId,
            count
        });
        return comments ?? [];
    }
}
