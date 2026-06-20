using Alife.Framework;
using Alife.Function.DeskPet;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

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
