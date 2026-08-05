using System.Runtime.CompilerServices;
using Alife.Framework;
using Alife.Function.DeskPet;
using Alife.Function.Interpreter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Alife.Test.DeskPet;

public class DeskPetServiceAdapterTests
{
    [Test]
    public async Task DeskPetService_UsesInjectedRuntimeForBodyActions()
    {
        FakeDeskPetRuntime runtime = new();
        await using ChatBot chatBot = new(null!, new ChatHistoryAgentThread());
        DeskPetService service = new(null!, runtime);
        service.Configuration = new DeskPetServiceConfig { EnableEmotionParameterSync = false };

        await service.AwakeAsync(new AwakeContext
        {
            Character = new Character { Name = "DeskPetTest" },
            ContextBuilder = new ChatHistoryAgentThread()
        });
        await service.StartAsync(Kernel.CreateBuilder().Build(), new ChatActivity(
            new Character { Name = "DeskPetTest" },
            Kernel.CreateBuilder().Build(),
            null!,
            chatBot,
            []));

        service.Expression("smile");
        service.Motion("wave");
        await service.ShowBubbleAsync("hello");
        await service.Move(10, 20, 1);

        Assert.That(runtime.Expressions, Is.EqualTo(new[] { "smile" }));
        Assert.That(runtime.Motions, Is.EqualTo(new[] { ("main", 1) }));
        Assert.That(runtime.Bubbles, Is.EqualTo(new[] { "hello" }));
        Assert.That(runtime.Moves, Is.EqualTo(new[] { (10d, 20d, 1) }));
    }

    [Test]
    public async Task DeskPetService_ShowsOneFallbackBubbleOnlyWithoutSpeak()
    {
        FakeDeskPetRuntime runtime = new();
        SequencedStreamingCompletionService completion = new();
        await using ChatBot chatBot = CreateChatBot(completion);
        await using DeskPetService service = new(null!, runtime);
        service.Configuration = new DeskPetServiceConfig { EnableEmotionParameterSync = false };
        completion.BeforeSecondResponse = () => service.Speak(new XmlExecutorContext
        {
            CallMode = CallMode.Content,
            Parameters = new Dictionary<string, string>(),
            CallChain = ["speak"],
            Content = "y"
        }, "y", CancellationToken.None);

        await service.AwakeAsync(new AwakeContext
        {
            Character = new Character { Name = "DeskPetTest" },
            ContextBuilder = new ChatHistoryAgentThread()
        });
        await service.StartAsync(Kernel.CreateBuilder().Build(), new ChatActivity(
            new Character { Name = "DeskPetTest" },
            Kernel.CreateBuilder().Build(),
            null!,
            chatBot,
            []));

        await chatBot.ChatInConversationAsync(ChatBot.LocalConversationId, "plain");
        await chatBot.ChatInConversationAsync(ChatBot.LocalConversationId, "explicit");
        await chatBot.ChatInConversationAsync(ChatBot.LocalConversationId, "tool-only");

        Assert.That(runtime.Bubbles, Is.EqualTo(new[] { "x", "y" }));
        await service.DestroyAsync();
    }

    static ChatBot CreateChatBot(IChatCompletionService completion)
    {
        IKernelBuilder builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(completion);
        Kernel kernel = builder.Build();
        return new ChatBot(new ChatCompletionAgent
        {
            Name = "test",
            Instructions = "test",
            Kernel = kernel
        }, new ChatHistoryAgentThread());
    }

    sealed class SequencedStreamingCompletionService : IChatCompletionService
    {
        int invocation;
        public Func<Task>? BeforeSecondResponse { get; set; }
        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatMessageContent>>([]);

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int current = Interlocked.Increment(ref invocation);
            if (current == 2 && BeforeSecondResponse != null)
                await BeforeSecondResponse();

            yield return new StreamingChatMessageContent(AuthorRole.Assistant, current switch
            {
                1 => "x",
                2 => "unused",
                _ => "<expression option=\"smile\" />"
            });
        }
    }

    sealed class FakeDeskPetRuntime : IDeskPetRuntime
    {
        public event Action<string>? OnInput;
        public event Action<string>? OnInteracted;

        public IEnumerable<string> SupportedExpressions => ["smile"];
        public IDictionary<string, (string Group, int Index)> SupportedMotions { get; } = new Dictionary<string, (string, int)>
        {
            ["wave"] = ("main", 1)
        };
        public List<string> Expressions { get; } = new();
        public List<(string Group, int Index)> Motions { get; } = new();
        public List<string> Bubbles { get; } = new();
        public List<(double X, double Y, int Duration)> Moves { get; } = new();

        public Task WaitReadyAsync() => Task.CompletedTask;
        public void ShowBubble(string text) => Bubbles.Add(text);
        public void HideBubble() {}
        public void PlayExpression(string? id) => Expressions.Add(id ?? "");
        public void PlayMotion(string group, int index) => Motions.Add((group, index));
        public void SendStatus(bool working) {}
        public void SetParams(Dictionary<string, float> parameters) {}
        public Task MoveAsync(double x, double y, int duration)
        {
            Moves.Add((x, y, duration));
            return Task.CompletedTask;
        }

        public Task<(double x, double y)> GetPositionAsync() => Task.FromResult((10d, 20d));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseInput(string text) => OnInput?.Invoke(text);
        public void RaiseInteraction(string text) => OnInteracted?.Invoke(text);
    }
}
