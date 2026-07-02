using Alife.Function.DataAgent;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentTraceDiagnosticsFormatterTests
{
    [Test]
    public void FormatEmitsStableTimelineDiagnostics()
    {
        DataAgentTraceTimeline timeline = Timeline(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            2,
            Terminal: false,
            [
                Event(
                    DataAgentTraceEventKind.RouteGate,
                    DataAgentTraceEventStatus.Succeeded,
                    "route_allowed",
                    queryAllowed: true,
                    executedSql: false,
                    terminal: false,
                    new Dictionary<string, string>
                    {
                        ["route_allowed"] = "true",
                        ["can_continue"] = "true"
                    }),
                Event(
                    DataAgentTraceEventKind.Execute,
                    DataAgentTraceEventStatus.Succeeded,
                    "read_only_query_executed",
                    queryAllowed: true,
                    executedSql: true,
                    terminal: false,
                    new Dictionary<string, string>
                    {
                        ["sql"] = "SELECT * FROM document_index"
                    }),
                Event(
                    DataAgentTraceEventKind.Checkpoint,
                    DataAgentTraceEventStatus.Succeeded,
                    "checkpoint_created",
                    queryAllowed: false,
                    executedSql: false,
                    terminal: false,
                    new Dictionary<string, string>
                    {
                        ["checkpoint"] = "created"
                    })
            ]);

        string text = DataAgentTraceDiagnosticsFormatter.Format(timeline);

        string[] expectedLines =
        [
            "DataAgent trace diagnostics",
            "session=session-1",
            "turn=2",
            "status=Active",
            "terminal=false",
            "events=3",
            "1 RouteGate Succeeded reason=route_allowed query_allowed=true executed_sql=false terminal=false can_continue=true route_allowed=true",
            "2 Execute Succeeded reason=read_only_query_executed query_allowed=true executed_sql=true terminal=false sql=redacted",
            "3 Checkpoint Succeeded reason=checkpoint_created query_allowed=false executed_sql=false terminal=false checkpoint=created"
        ];

        Assert.Multiple(() =>
        {
            Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("document_index"));
        });
    }

    [Test]
    public void FormatUnavailableEmitsStableState()
    {
        string text = DataAgentTraceDiagnosticsFormatter.Format(null);

        string[] expectedLines =
        [
            "DataAgent trace diagnostics",
            "state=unavailable",
            "reason=trace_unavailable"
        ];
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [TestCase("Bearer token-abcdef123456", "Bearer", "token-abcdef123456")]
    [TestCase("Server=db.internal;Uid=alife;Pwd=secret", "db.internal", "secret")]
    [TestCase("[tool_route_context]\nAllowed XML tools: dataagent_query\n[/tool_route_context]", "tool_route_context", "dataagent_query")]
    [TestCase("[data_agent_evidence_pack]\ntrace=unsafe\n[/data_agent_evidence_pack]", "data_agent_evidence_pack", "trace=unsafe")]
    [TestCase("api_key=sk-test", "api_key", "sk-test")]
    [TestCase("[hidden_context]secret[/hidden_context]", "hidden_context", "secret")]
    [TestCase("hidden context: internal route decision", "hidden context", "internal route decision")]
    [TestCase("Allowed XML tool text: <dataagent_query>", "Allowed XML tool text", "dataagent_query")]
    [TestCase("api_key: sk-test", "api_key", "sk-test")]
    [TestCase("OPENAI_API_KEY: sk-test", "OPENAI_API_KEY", "sk-test")]
    [TestCase("token: abcdef123456", "token", "abcdef123456")]
    [TestCase("sk-ORklQekAufyaZ26NHLLXiLnHUvVfake", "sk-", "ORklQekAufyaZ26NHLLXiLnHUvVfake")]
    [TestCase("SELECT COUNT(*) FROM users", "SELECT", "users")]
    public void FormatRedactsUnsafeFactValues(string unsafeValue, string firstUnsafeSubstring, string secondUnsafeSubstring)
    {
        DataAgentTraceTimeline timeline = Timeline(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            1,
            Terminal: false,
            [
                Event(
                    DataAgentTraceEventKind.RouteGate,
                    DataAgentTraceEventStatus.Succeeded,
                    "route_allowed",
                    queryAllowed: true,
                    executedSql: false,
                    terminal: false,
                    new Dictionary<string, string>
                    {
                        ["detail"] = unsafeValue
                    })
            ]);

        string text = DataAgentTraceDiagnosticsFormatter.Format(timeline);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("detail=redacted"));
            Assert.That(text, Does.Not.Contain(firstUnsafeSubstring));
            Assert.That(text, Does.Not.Contain(secondUnsafeSubstring));
        });
    }

    [Test]
    public void FormatRedactsTableAndDatasetFactValues()
    {
        DataAgentTraceTimeline timeline = Timeline(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            1,
            Terminal: false,
            [
                Event(
                    DataAgentTraceEventKind.Execute,
                    DataAgentTraceEventStatus.Succeeded,
                    "read_only_query_executed",
                    queryAllowed: true,
                    executedSql: true,
                    terminal: false,
                    new Dictionary<string, string>
                    {
                        ["table"] = "document_index",
                        ["source_table"] = "users",
                        ["dataset"] = "finance_records",
                        ["rows"] = "3",
                        ["route_allowed"] = "true",
                        ["can_continue"] = "true"
                    })
            ]);

        string text = DataAgentTraceDiagnosticsFormatter.Format(timeline);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("can_continue=true"));
            Assert.That(text, Does.Contain("dataset=redacted"));
            Assert.That(text, Does.Contain("route_allowed=true"));
            Assert.That(text, Does.Contain("rows=3"));
            Assert.That(text, Does.Contain("source_table=redacted"));
            Assert.That(text, Does.Contain("table=redacted"));
            Assert.That(text, Does.Not.Contain("document_index"));
            Assert.That(text, Does.Not.Contain("finance_records"));
            Assert.That(text, Does.Not.Contain("users"));
        });
    }

    [Test]
    public void FormatEmitsTimelineTurnCountFaithfully()
    {
        DataAgentTraceTimeline timeline = Timeline(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            -1,
            Terminal: false,
            [
                Event(
                    DataAgentTraceEventKind.RouteGate,
                    DataAgentTraceEventStatus.Succeeded,
                    "route_allowed",
                    queryAllowed: true,
                    executedSql: false,
                    terminal: false,
                    new Dictionary<string, string>
                    {
                        ["route_allowed"] = "true"
                    })
            ]);

        string text = DataAgentTraceDiagnosticsFormatter.Format(timeline);

        Assert.That(text.Split(Environment.NewLine)[2], Is.EqualTo("turn=-1"));
    }

    [Test]
    public void FormatBoundsLongTraceText()
    {
        List<DataAgentTraceEvent> events = [];
        for (int i = 0; i < 100; i++)
        {
            events.Add(Event(
                DataAgentTraceEventKind.RouteGate,
                DataAgentTraceEventStatus.Succeeded,
                $"route_allowed_{i}",
                queryAllowed: true,
                executedSql: false,
                terminal: false,
                new Dictionary<string, string>
                {
                    ["detail"] = new string('x', 80)
                }));
        }

        DataAgentTraceTimeline timeline = Timeline(
            "session-1",
            DataAgentAnalysisSessionStatus.Active,
            1,
            Terminal: false,
            events);

        string text = DataAgentTraceDiagnosticsFormatter.Format(timeline, maxChars: 1800);

        Assert.Multiple(() =>
        {
            Assert.That(text.Length, Is.LessThanOrEqualTo(1800));
            Assert.That(text, Does.EndWith("..."));
        });
    }

    static DataAgentTraceTimeline Timeline(
        string sessionId,
        DataAgentAnalysisSessionStatus status,
        int turn,
        bool Terminal,
        IReadOnlyList<DataAgentTraceEvent> events)
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");
        return new DataAgentTraceTimeline(
            sessionId,
            status,
            turn,
            now,
            now.AddMilliseconds(10),
            Terminal,
            events);
    }

    static DataAgentTraceEvent Event(
        DataAgentTraceEventKind kind,
        DataAgentTraceEventStatus status,
        string reason,
        bool queryAllowed,
        bool executedSql,
        bool terminal,
        IReadOnlyDictionary<string, string> facts)
    {
        return new DataAgentTraceEvent(
            kind,
            status,
            reason,
            executedSql,
            queryAllowed,
            terminal,
            facts);
    }
}
