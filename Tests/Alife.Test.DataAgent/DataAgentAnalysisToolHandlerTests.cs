using Alife.Function.DataAgent;
using Alife.Function.Interpreter;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentAnalysisToolHandlerTests
{
    static readonly DateTimeOffset Now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void StartReturnsAndPublishesAnalysisSessionContext()
    {
        List<string> published = [];
        DataAgentAnalysisToolHandler handler = new(CreateService(_ => AcceptedAnswer()), published.Add);

        string context = handler.Start("xiayu", "Which documents describe DataAgent?");

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("[data_agent_analysis_session_context]"));
            Assert.That(context, Does.Contain("caller_id=xiayu"));
            Assert.That(context, Does.Contain("goal=Which documents describe DataAgent?"));
            Assert.That(context, Does.Contain("[data_agent_context]"));
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published.Single(), Is.EqualTo(context));
        });
    }

    [Test]
    public void ContinueReturnsAndPublishesAnalysisSessionContext()
    {
        List<string> published = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisToolHandler handler = new(CreateService(_ => AcceptedAnswer(), store), published.Add);
        string startContext = handler.Start("xiayu", "Which tests failed?");
        string sessionId = GetContextValue(startContext, "session_id");

        string context = handler.Continue(sessionId, "continue");

        Assert.Multiple(() =>
        {
            Assert.That(context, Does.Contain("[data_agent_analysis_session_context]"));
            Assert.That(context, Does.Contain($"session_id={sessionId}"));
            Assert.That(context, Does.Contain("turn_count=2"));
            Assert.That(context, Does.Contain("[data_agent_context]"));
            Assert.That(published, Has.Count.EqualTo(2));
            Assert.That(published.Last(), Is.EqualTo(context));
        });
    }

    [Test]
    public void SummarizeUsesAnalysisServiceAndDoesNotCallAnswerBoundary()
    {
        int answerCalls = 0;
        List<string> published = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisToolHandler handler = new(
            CreateService(_ =>
            {
                answerCalls++;
                return AcceptedAnswer();
            }, store),
            published.Add);
        string startContext = handler.Start("xiayu", "Which tests failed?");
        string sessionId = GetContextValue(startContext, "session_id");

        string context = handler.Summarize(sessionId);

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(context, Does.Contain("status=Summarized"));
            Assert.That(context, Does.Contain("turn_count=2"));
            Assert.That(context, Does.Contain("last_summary="));
            Assert.That(published, Has.Count.EqualTo(2));
            Assert.That(published.Last(), Is.EqualTo(context));
        });
    }

    [Test]
    public void EndUsesAnalysisServiceAndDoesNotCallAnswerBoundary()
    {
        int answerCalls = 0;
        List<string> published = [];
        InMemoryDataAgentAnalysisSessionStore store = new();
        DataAgentAnalysisToolHandler handler = new(
            CreateService(_ =>
            {
                answerCalls++;
                return AcceptedAnswer();
            }, store),
            published.Add);
        string startContext = handler.Start("xiayu", "Which tests failed?");
        string sessionId = GetContextValue(startContext, "session_id");

        string context = handler.End(sessionId);

        Assert.Multiple(() =>
        {
            Assert.That(answerCalls, Is.EqualTo(1));
            Assert.That(context, Does.Contain("status=Ended"));
            Assert.That(context, Does.Contain("turn_count=2"));
            Assert.That(context, Does.Contain("last_summary="));
            Assert.That(published, Has.Count.EqualTo(2));
            Assert.That(published.Last(), Is.EqualTo(context));
        });
    }

    [Test]
    public void AnalysisMethodsAreRegisteredAsXmlFunctions()
    {
        XmlHandler xmlHandler = new(new DataAgentAnalysisToolHandler(CreateService(_ => AcceptedAnswer())));

        Assert.Multiple(() =>
        {
            Assert.That(xmlHandler.Functions.Select(function => function.Name), Is.EquivalentTo(new[]
            {
                "dataagent_analysis_start",
                "dataagent_analysis_continue",
                "dataagent_analysis_summarize",
                "dataagent_analysis_end"
            }));
            Assert.That(xmlHandler.Functions, Has.All.Matches<XmlFunction>(
                function => function.Mode == FunctionMode.OneShot));
            Assert.That(xmlHandler.FunctionDocument(), Does.Contain("<dataagent_analysis_start"));
            Assert.That(xmlHandler.FunctionDocument(), Does.Contain("<dataagent_analysis_continue"));
            Assert.That(xmlHandler.FunctionDocument(), Does.Contain("<dataagent_analysis_summarize"));
            Assert.That(xmlHandler.FunctionDocument(), Does.Contain("<dataagent_analysis_end"));
        });
    }

    [Test]
    public void AnalysisMethodsRejectBlankRequiredArguments()
    {
        DataAgentAnalysisToolHandler handler = new(CreateService(_ => AcceptedAnswer()));

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => handler.Start(" ", "question"));
            Assert.Throws<ArgumentException>(() => handler.Start("caller", " "));
            Assert.Throws<ArgumentException>(() => handler.Continue(" ", "question"));
            Assert.Throws<ArgumentException>(() => handler.Continue("session", " "));
            Assert.Throws<ArgumentException>(() => handler.Summarize(" "));
            Assert.Throws<ArgumentException>(() => handler.End(" "));
        });
    }

    static DataAgentAnalysisService CreateService(
        Func<string, DataAgentAnswer> answer,
        InMemoryDataAgentAnalysisSessionStore? store = null)
    {
        return new DataAgentAnalysisService(
            answer,
            store ?? new InMemoryDataAgentAnalysisSessionStore(),
            new DataAgentFollowUpInterpreter(),
            () => Now);
    }

    static DataAgentAnswer AcceptedAnswer(string summary = "Found DataAgent documentation.")
    {
        return new DataAgentAnswer(
            "document_index",
            "SELECT path FROM document_index LIMIT 20",
            2,
            summary,
            "[data_agent_context]\nsql_status=validated\n[/data_agent_context]",
            true,
            string.Empty,
            new DataAgentPlannerExplanation(
                "TestPlanner",
                "find_documents",
                "document_index",
                "high",
                ["test"],
                "test accepted answer"));
    }

    static string GetContextValue(string context, string field)
    {
        string prefix = $"{field}=";
        string? line = context
            .Split(Environment.NewLine)
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));

        Assert.That(line, Is.Not.Null);
        return line![prefix.Length..];
    }
}
