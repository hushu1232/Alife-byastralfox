using Alife.Function.MessageFilter;
using NUnit.Framework;

namespace Alife.Test.Framework;

public class PromptDynamicTailComposerTests
{
    [Test]
    public void BuildQqTail_ContainsCurrentMessageAndRoutingWithoutPersona()
    {
        PromptDynamicTailComposer composer = new();

        string tail = composer.BuildQqTail(
            currentMessage: "[925402131(史莱克学院)] 群友：夏羽你好",
            routingHint: "message_tone=friendly\nsocial_action=friendly_short_reply",
            memorySnippet: "群友对夏羽态度友好。",
            toolResult: "");

        Assert.That(tail, Does.Contain("[dynamic QQ turn tail]"));
        Assert.That(tail, Does.Contain("夏羽你好"));
        Assert.That(tail, Does.Contain("message_tone=friendly"));
        Assert.That(tail, Does.Contain("群友对夏羽态度友好"));
        Assert.That(tail, Does.Not.Contain("你是夏羽"));
        Assert.That(tail, Does.Not.Contain("[stable character prefix]"));
    }
}
