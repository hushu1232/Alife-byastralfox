using Alife.Function.Interpreter;
using Alife.Framework;
using Microsoft.SemanticKernel.Agents;

[TestFixture]
public class XmlStreamExecutorTests
{
    class TestHandler
    {
        public List<string> Logs { get; } = new();

        [XmlFunction(FunctionMode.Content)]
        public void Test(XmlExecutorContext context)
        {
            Logs.Add($"test:{context.CallMode}:{context.Content}");
        }

        [XmlFunction(FunctionMode.OneShot)]
        public void OneShot(XmlExecutorContext context)
        {
            Logs.Add($"oneshot:{context.CallMode}");
        }

        [XmlFunction(FunctionMode.Content)]
        public void Script(XmlExecutorContext context, int timeout)
        {
            if (context.CallMode != CallMode.Closing)
                return;

            Logs.Add($"script:{timeout} {context.FullContent}");
        }
    }

    class ConversationHandler(ChatBot chatBot)
    {
        public string? ObservedConversationId { get; private set; }

        [XmlFunction(FunctionMode.OneShot)]
        public void Capture()
        {
            ObservedConversationId = chatBot.CurrentConversationId;
        }
    }

    [Test]
    public async Task TestExecutorBasic()
    {
        TestHandler handler = new TestHandler();
        XmlHandlerTable table = new XmlHandlerTable();
        table.Register(new XmlHandler(handler));

        XmlStreamParser parser = new XmlStreamParser("script");
        // Set minBreakingLength to 1 to force split on every separator
        await using XmlStreamExecutor executor = new XmlStreamExecutor(parser, table, [".", "!"], 1);

        executor.Feed("<test>Hello. World!</test>");
        executor.Feed("<script timeout=\"20\">if(0 < 1) print(123 > 1)</ script>");
        executor.Flush();

        while (executor.IsInactive == false)
        {
            await Task.Delay(200);
        }

        // Logs should contain Content calls for each segment and a Closing call
        Assert.That(handler.Logs, Has.Member("test:Content:Hello."));
        Assert.That(handler.Logs, Has.Member("test:Content: World!"));
        Assert.That(handler.Logs, Has.Member("test:Closing:"));
        Assert.That(handler.Logs, Has.Member("script:20 if(0 < 1) print(123 > 1)"));
    }

    [Test]
    public async Task TestOneShot()
    {
        TestHandler handler = new TestHandler();
        XmlHandlerTable table = new XmlHandlerTable();
        table.Register(new XmlHandler(handler));

        XmlStreamParser parser = new XmlStreamParser();
        await using XmlStreamExecutor executor = new XmlStreamExecutor(parser, table, [], 100);

        executor.Feed("<oneshot />");
        executor.Flush();

        await Task.Delay(200);

        Assert.That(handler.Logs, Has.Member("oneshot:OneShot"));
    }

    [Test]
    public async Task CompletedOneShotReportsSafeToolOutcome()
    {
        TestHandler handler = new TestHandler();
        XmlHandlerTable table = new XmlHandlerTable();
        table.Register(new XmlHandler(handler));
        XmlStreamParser parser = new XmlStreamParser();
        await using XmlStreamExecutor executor = new XmlStreamExecutor(parser, table, [], 100);
        List<(string Name, bool Succeeded)> outcomes = [];
        System.Reflection.EventInfo? eventInfo = typeof(XmlStreamExecutor).GetEvent("ToolCompleted");

        Assert.That(eventInfo, Is.Not.Null, "Completed tool calls must report a model-safe outcome.");
        eventInfo!.AddEventHandler(executor, (Action<string, bool>)((name, succeeded) => outcomes.Add((name, succeeded))));

        executor.Feed("<oneshot />");
        executor.Flush();
        while (executor.IsInactive == false)
            await Task.Delay(20);

        Assert.That(outcomes, Is.EqualTo([("oneshot", true)]));
    }

    [Test]
    public async Task DuplicateOneShotCallsRunOncePerModelTurn()
    {
        TestHandler handler = new();
        XmlHandlerTable table = new();
        table.Register(new XmlHandler(handler));
        XmlStreamParser parser = new();
        await using XmlStreamExecutor executor = new(parser, table, [], 100);

        executor.Feed("<oneshot /><oneshot />");
        executor.Flush();
        while (executor.IsInactive == false)
            await Task.Delay(20);

        Assert.That(handler.Logs.Count(log => log == "oneshot:OneShot"), Is.EqualTo(1));

        executor.Feed("<oneshot />");
        executor.Flush();
        while (executor.IsInactive == false)
            await Task.Delay(20);

        Assert.That(handler.Logs.Count(log => log == "oneshot:OneShot"), Is.EqualTo(2));
    }

    [Test]
    public async Task TestNestedTags()
    {
        TestHandler handler = new TestHandler();
        XmlHandlerTable table = new XmlHandlerTable();
        table.Register(new XmlHandler(handler));

        XmlStreamParser parser = new XmlStreamParser();
        await using XmlStreamExecutor executor = new XmlStreamExecutor(parser, table, [], 100);

        executor.Feed("<test><test>nested</test></test>");
        executor.Flush();

        await Task.Delay(200);

        // Outer tag gets "nested" too because of AboveContent logic
        // But the immediate call for the inner tag will be Content:nested, Closing:
        // And the outer tag will eventually get Content:nested, Closing:

        Assert.That(handler.Logs, Has.Count.AtLeast(4));
    }

    [Test]
    public async Task ToolExecutionKeepsTheOriginatingConversationScope()
    {
        await using ChatBot chatBot = new(null!, new ChatHistoryAgentThread());
        ConversationHandler handler = new(chatBot);
        XmlHandlerTable table = new();
        table.Register(new XmlHandler(handler));
        XmlStreamParser parser = new();
        await using XmlStreamExecutor executor = new(
            parser,
            table,
            [],
            100,
            conversationId => chatBot.UseConversation(conversationId ?? ChatBot.DefaultConversationId));

        executor.Feed("<capture />", ChatBot.LocalConversationId);
        executor.Flush(ChatBot.LocalConversationId);
        while (executor.IsInactive == false)
            await Task.Delay(20);

        Assert.That(handler.ObservedConversationId, Is.EqualTo(ChatBot.LocalConversationId));
    }
}
