using System.Text.Json;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class OneBotQZoneRuntimeTests
{
    [Test]
    public async Task PublishPost_UsesConfiguredPostAction()
    {
        FakeActionInvoker invoker = new();
        OneBotQZoneRuntime runtime = new(invoker);

        await runtime.PublishPost("hello");

        Assert.That(invoker.Calls, Has.Count.EqualTo(1));
        Assert.That(invoker.Calls[0].Action, Is.EqualTo("send_msg"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"message\":\"hello\""));
    }

    [Test]
    public async Task Comment_MapsTargetPostAndContent()
    {
        FakeActionInvoker invoker = new();
        OneBotQZoneRuntime runtime = new(invoker);

        await runtime.Comment(1001, "post-a", "nice");

        Assert.That(invoker.Calls, Has.Count.EqualTo(1));
        Assert.That(invoker.Calls[0].Action, Is.EqualTo("send_comment"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_uin\":1001"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_tid\":\"post-a\""));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"content\":\"nice\""));
    }

    [Test]
    public async Task ReplyComment_IncludesCommentId()
    {
        FakeActionInvoker invoker = new();
        OneBotQZoneRuntime runtime = new(invoker);

        await runtime.ReplyComment(1001, "post-a", "comment-b", "thanks");

        Assert.That(invoker.Calls, Has.Count.EqualTo(1));
        Assert.That(invoker.Calls[0].Action, Is.EqualTo("send_comment"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_uin\":1001"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_tid\":\"post-a\""));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"comment_id\":\"comment-b\""));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"content\":\"thanks\""));
    }

    [Test]
    public async Task LikePost_UsesConfiguredLikeAction()
    {
        FakeActionInvoker invoker = new();
        OneBotQZoneRuntime runtime = new(invoker);

        await runtime.LikePost(1001, "post-a");

        Assert.That(invoker.Calls, Has.Count.EqualTo(1));
        Assert.That(invoker.Calls[0].Action, Is.EqualTo("send_like"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_uin\":1001"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_tid\":\"post-a\""));
    }

    [Test]
    public async Task GetLatestPost_UsesConfiguredReadAction()
    {
        FakeActionInvoker invoker = new();
        invoker.Enqueue(new QZonePostSnapshot("post-a", 1001, "latest"));
        OneBotQZoneRuntime runtime = new(invoker, new OneBotQZoneRuntimeOptions
        {
            LatestPostAction = "get_qzone_latest_post"
        });

        QZonePostSnapshot? post = await runtime.GetLatestPost(1001);

        Assert.That(post?.PostId, Is.EqualTo("post-a"));
        Assert.That(invoker.Calls, Has.Count.EqualTo(1));
        Assert.That(invoker.Calls[0].Action, Is.EqualTo("get_qzone_latest_post"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_uin\":1001"));
    }

    [Test]
    public async Task GetLatestComments_UsesConfiguredReadAction()
    {
        FakeActionInvoker invoker = new();
        invoker.Enqueue(new List<QZoneCommentSnapshot>
        {
            new("comment-a", 2001, "first")
        });
        OneBotQZoneRuntime runtime = new(invoker, new OneBotQZoneRuntimeOptions
        {
            LatestCommentsAction = "get_qzone_comments"
        });

        IReadOnlyList<QZoneCommentSnapshot> comments = await runtime.GetLatestComments(1001, "post-a", 5);

        Assert.That(comments.Select(comment => comment.CommentId), Is.EqualTo(new[] { "comment-a" }));
        Assert.That(invoker.Calls, Has.Count.EqualTo(1));
        Assert.That(invoker.Calls[0].Action, Is.EqualTo("get_qzone_comments"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_uin\":1001"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_tid\":\"post-a\""));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"count\":5"));
    }

    sealed class FakeActionInvoker : IOneBotActionInvoker
    {
        public List<(string Action, string Json)> Calls { get; } = new();
        readonly Queue<object?> responses = new();

        public void Enqueue(object? response)
        {
            responses.Enqueue(response);
        }

        public Task<T?> CallActionAsync<T>(string action, object? parameters = null)
        {
            Calls.Add((action, JsonSerializer.Serialize(parameters)));
            if (responses.Count == 0)
                return Task.FromResult<T?>(default);

            return Task.FromResult((T?)responses.Dequeue());
        }
    }
}
