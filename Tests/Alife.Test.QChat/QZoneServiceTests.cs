using Alife.Framework;
using Alife.Function.QChat;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using NUnit.Framework;
using System.Reflection;
using System.Text.Json;

namespace Alife.Test.QChat;

[TestFixture]
public class QZoneServiceTests
{
    [Test]
    public async Task QZonePost_DryRunDoesNotCallRuntime()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = true
            }
        };

        QZoneActionResult result = await service.QZonePost("hello qzone");

        Assert.That(result.Executed, Is.False);
        Assert.That(result.Action, Is.EqualTo("post"));
        Assert.That(runtime.Posts, Is.Empty);
    }

    [Test]
    public async Task QZoneComment_CallsRuntimeForAllowedTarget()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                AllowedQZoneTargetIds = "1001",
                DryRunExternalActions = false
            }
        };

        QZoneActionResult result = await service.QZoneComment(1001, "post-a", "nice");

        Assert.That(result.Executed, Is.True);
        Assert.That(runtime.Comments, Is.EqualTo(new[] { (1001L, "post-a", "nice") }));
    }

    [Test]
    public async Task QZoneLike_SkipsTargetsOutsidePrivateChatContactPool()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 1.0
            }
        };

        QZoneActionResult result = await service.QZoneLike(2001, "post-a", () => 0.0);

        Assert.That(result.Executed, Is.False);
        Assert.That(result.Reason, Does.Contain("private chat contact"));
        Assert.That(runtime.Likes, Is.Empty);
    }

    [Test]
    public async Task QZoneLike_SkipsSameTargetDuringConfiguredCooldown()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T12:00:00Z");
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime, clock: () => now)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 1.0,
                QZoneTargetCooldownMinutes = 30,
                DryRunExternalActions = false
            }
        };

        QZoneActionResult first = await service.QZoneLike(1001, "post-a", () => 0.0);
        QZoneActionResult second = await service.QZoneLike(1001, "post-b", () => 0.0);

        Assert.That(first.Executed, Is.True);
        Assert.That(second.Executed, Is.False);
        Assert.That(second.Reason, Does.Contain("cooldown"));
        Assert.That(runtime.Likes, Is.EqualTo(new[] { (1001L, "post-a") }));
    }

    [Test]
    public async Task QZoneComment_SkipsSameTargetAfterDailyLimit()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-06-14T12:00:00Z");
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime, clock: () => now)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                MaxQZoneInteractionsPerTargetPerDay = 1,
                DryRunExternalActions = false
            }
        };

        QZoneActionResult first = await service.QZoneComment(1001, "post-a", "nice");
        QZoneActionResult second = await service.QZoneComment(1001, "post-b", "thanks");

        Assert.That(first.Executed, Is.True);
        Assert.That(second.Executed, Is.False);
        Assert.That(second.Reason, Does.Contain("daily limit"));
        Assert.That(runtime.Comments, Is.EqualTo(new[] { (1001L, "post-a", "nice") }));
    }

    [Test]
    public async Task QZoneReplyComment_CallsRuntimeWhenMostlyReplyPolicyAllows()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                CommentReplyProbability = 0.8,
                DryRunExternalActions = false
            }
        };

        QZoneActionResult result = await service.QZoneReplyComment(1001, "post-a", "comment-b", "thanks", () => 0.5);

        Assert.That(result.Executed, Is.True);
        Assert.That(runtime.Replies, Is.EqualTo(new[] { (1001L, "post-a", "comment-b", "thanks") }));
    }

    [Test]
    public async Task QZoneComment_UsesInjectedOneBotActionInvokerWhenRuntimeIsAbsent()
    {
        FakeActionInvoker invoker = new();
        QZoneService service = new(actionInvoker: invoker)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = false
            }
        };

        QZoneActionResult result = await service.QZoneComment(1001, "post-a", "nice");

        Assert.That(result.Executed, Is.True);
        Assert.That(invoker.Calls, Has.Count.EqualTo(1));
        Assert.That(invoker.Calls[0].Action, Is.EqualTo("send_comment"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_uin\":1001"));
        Assert.That(invoker.Calls[0].Json, Does.Contain("\"target_tid\":\"post-a\""));
    }

    [Test]
    public async Task ConnectAsync_ConfiguresAndConnectsInjectedActionConnection()
    {
        FakeActionConnection connection = new();
        QZoneService service = new(actionConnection: connection)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                DryRunExternalActions = false,
                Url = "ws://127.0.0.1:3010",
                Token = "secret"
            }
        };

        await service.ConnectAsync();

        Assert.That(connection.Url, Is.EqualTo("ws://127.0.0.1:3010"));
        Assert.That(connection.Token, Is.EqualTo("secret"));
        Assert.That(connection.ConnectCalls, Is.EqualTo(1));
        Assert.That(connection.IsConnected, Is.True);
        Assert.That(service.GetHealth().Status, Is.EqualTo(ModuleHealthStatus.Healthy));
    }

    [Test]
    public async Task QZoneLatestPostAndComments_ReadsLatestPostThenComments()
    {
        FakeQZoneRuntime runtime = new()
        {
            LatestPost = new QZonePostSnapshot("post-a", 1001, "latest post"),
            LatestComments =
            [
                new QZoneCommentSnapshot("comment-a", 2001, "first"),
                new QZoneCommentSnapshot("comment-b", 2002, "second")
            ]
        };
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                AllowedQZoneTargetIds = "1001"
            }
        };

        QZoneQueryResult result = await service.QZoneLatestPostAndComments(1001, 2);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Post?.PostId, Is.EqualTo("post-a"));
        Assert.That(result.Comments.Select(comment => comment.CommentId), Is.EqualTo(new[] { "comment-a", "comment-b" }));
        Assert.That(runtime.LatestPostRequests, Is.EqualTo(new[] { 1001L }));
        Assert.That(runtime.LatestCommentRequests, Is.EqualTo(new[] { (1001L, "post-a", 2) }));
        Assert.That(runtime.Comments, Is.Empty);
        Assert.That(runtime.Likes, Is.Empty);
    }

    [Test]
    public async Task QZoneReportFeedbackDoesNotPokeInternalQqZoneLabel()
    {
        FakeQZoneRuntime runtime = new();
        QZoneService service = new(runtime)
        {
            Configuration = new QZoneServiceConfig
            {
                EnableQZone = true,
                PrivateChatContactIds = "1001",
                PrivateContactLikeProbability = 0.0
            }
        };
        StartService(service);

        QZoneActionResult result = await service.QZoneLike(1001, "post-a", () => 0.5);

        string pending = GetPendingPokeText(service);
        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.False);
            Assert.That(pending, Does.Not.Contain("[QQ Zone"));
            Assert.That(pending, Does.Not.Contain("qzone-"));
            Assert.That(pending, Does.Contain("QZone action skipped"));
            Assert.That(pending, Does.Contain("skipped by random like probability policy"));
        });
    }

    [Test]
    public async Task QZoneProactiveFeedbackDoesNotPokeInternalQqZoneLabel()
    {
        QZoneService service = new(new FakeQZoneRuntime());
        StartService(service);

        QZoneProactiveExecutionResult result = await service.ExecuteConfirmedProactiveSuggestion("missing-id");

        string pending = GetPendingPokeText(service);
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(pending, Does.Not.Contain("[QQ Zone"));
            Assert.That(pending, Does.Not.Contain("rejected"));
            Assert.That(pending, Does.Contain("QZone proactive action was not executed"));
            Assert.That(pending, Does.Contain("Proactive behavior service is unavailable"));
        });
    }

    sealed class FakeQZoneRuntime : IQZoneRuntime
    {
        public List<string> Posts { get; } = new();
        public List<(long TargetId, string PostId, string Content)> Comments { get; } = new();
        public List<(long TargetId, string PostId, string CommentId, string Content)> Replies { get; } = new();
        public List<(long TargetId, string PostId)> Likes { get; } = new();
        public List<long> LatestPostRequests { get; } = new();
        public List<(long TargetId, string PostId, int Count)> LatestCommentRequests { get; } = new();
        public QZonePostSnapshot? LatestPost { get; init; }
        public IReadOnlyList<QZoneCommentSnapshot> LatestComments { get; init; } = [];

        public Task PublishPost(string content)
        {
            Posts.Add(content);
            return Task.CompletedTask;
        }

        public Task Comment(long targetId, string postId, string content)
        {
            Comments.Add((targetId, postId, content));
            return Task.CompletedTask;
        }

        public Task ReplyComment(long targetId, string postId, string commentId, string content)
        {
            Replies.Add((targetId, postId, commentId, content));
            return Task.CompletedTask;
        }

        public Task LikePost(long targetId, string postId)
        {
            Likes.Add((targetId, postId));
            return Task.CompletedTask;
        }

        public Task<QZonePostSnapshot?> GetLatestPost(long targetId)
        {
            LatestPostRequests.Add(targetId);
            return Task.FromResult(LatestPost);
        }

        public Task<IReadOnlyList<QZoneCommentSnapshot>> GetLatestComments(long targetId, string postId, int count)
        {
            LatestCommentRequests.Add((targetId, postId, count));
            return Task.FromResult(LatestComments);
        }
    }

    sealed class FakeActionInvoker : IOneBotActionInvoker
    {
        public List<(string Action, string Json)> Calls { get; } = new();

        public Task<T?> CallActionAsync<T>(string action, object? parameters = null)
        {
            Calls.Add((action, JsonSerializer.Serialize(parameters)));
            return Task.FromResult<T?>(default);
        }
    }

    sealed class FakeActionConnection : IOneBotActionConnection
    {
        public bool IsConnected { get; private set; }
        public string Url { get; set; } = "";
        public string Token { get; set; } = "";
        public int ConnectCalls { get; private set; }

        public Task ConnectAsync()
        {
            ConnectCalls++;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task<T?> CallActionAsync<T>(string action, object? parameters = null)
        {
            return Task.FromResult<T?>(default);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    static void StartService(QZoneService service)
    {
        Character character = new() { Name = "QZoneTest" };
        ChatHistoryAgentThread thread = new();
        service.AwakeAsync(new AwakeContext
        {
            Character = character,
            ContextBuilder = thread,
            KernelBuilder = Kernel.CreateBuilder(),
        }).GetAwaiter().GetResult();
        ChatBot chatBot = new(null!, thread);
        service.StartAsync(Kernel.CreateBuilder().Build(), new ChatActivity(
            character,
            Kernel.CreateBuilder().Build(),
            null!,
            chatBot,
            [])).GetAwaiter().GetResult();
    }

    static string GetPendingPokeText(QZoneService service)
    {
        PropertyInfo chatBotProperty = typeof(InteractiveModule)
            .GetProperty("ChatBot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ChatBot chatBot = (ChatBot)chatBotProperty.GetValue(service)!;
        FieldInfo messageCacheField = typeof(ChatBot)
            .GetField("messageCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IEnumerable<string> messages = (IEnumerable<string>)messageCacheField.GetValue(chatBot)!;
        return string.Join("", messages);
    }
}
