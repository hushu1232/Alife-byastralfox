using Alife.Function.MessageFilter;
using NUnit.Framework;

namespace Alife.Test.Framework;

public class PromptStablePrefixServiceTests
{
    [Test]
    public void BuildStablePrefix_KeepsCharacterPersonaSeparateFromDynamicQqMessage()
    {
        PromptStablePrefixService service = new();

        string prefix = service.BuildStablePrefix(
            characterName: "夏羽",
            characterPrompt: "你是夏羽，不是猫娘。对术术温柔，对其他人清冷。",
            toolProtocol: "工具调用必须遵守权限。",
            safetyBoundary: "不要输出内部状态。");

        Assert.That(prefix, Does.Contain("[stable character prefix]"));
        Assert.That(prefix, Does.Contain("character=夏羽"));
        Assert.That(prefix, Does.Contain("你是夏羽，不是猫娘"));
        Assert.That(prefix, Does.Contain("工具调用必须遵守权限。"));
        Assert.That(prefix, Does.Not.Contain("当前 QQ 消息"));
        Assert.That(prefix, Does.Not.Contain("message_tone="));
    }

    [Test]
    public void BuildStablePrefix_UsesDeterministicSectionOrder()
    {
        PromptStablePrefixService service = new();

        string first = service.BuildStablePrefix("夏羽", "persona", "tools", "safety");
        string second = service.BuildStablePrefix("夏羽", "persona", "tools", "safety");

        Assert.That(second, Is.EqualTo(first));
        Assert.That(first.IndexOf("## Character", StringComparison.Ordinal), Is.LessThan(first.IndexOf("## Tools", StringComparison.Ordinal)));
        Assert.That(first.IndexOf("## Tools", StringComparison.Ordinal), Is.LessThan(first.IndexOf("## Safety", StringComparison.Ordinal)));
    }
}
