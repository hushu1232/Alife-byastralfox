using Alife.Function.DataAgent;
using NUnit.Framework;

namespace Alife.Test.DataAgent;

[TestFixture]
public sealed class DataAgentProgressDiagnosticsFormatterTests
{
    [Test]
    public void FormatUnavailableEmitsStableState()
    {
        string text = DataAgentProgressDiagnosticsFormatter.Format([], "session-a", Now());

        string[] expectedLines =
        [
            "DataAgent progress diagnostics",
            "state=unavailable",
            "reason=progress_unavailable"
        ];
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [Test]
    public void FormatListsRecentEventsInOrder()
    {
        DateTimeOffset now = Now();
        DataAgentProgressEvent[] events =
        [
            Event(
                DataAgentProgressEventKind.RouteGate,
                DataAgentProgressEventPhase.Started,
                DataAgentProgressEventStatus.Running,
                "route_gate_started",
                now.AddSeconds(-2),
                executedSql: false,
                queryAllowed: true,
                terminal: false,
                new Dictionary<string, string>
                {
                    ["route_allowed"] = "true"
                }),
            Event(
                DataAgentProgressEventKind.Execute,
                DataAgentProgressEventPhase.Completed,
                DataAgentProgressEventStatus.Succeeded,
                "read_only_query_executed",
                now.AddSeconds(-1),
                executedSql: true,
                queryAllowed: true,
                terminal: false,
                new Dictionary<string, string>
                {
                    ["rows"] = "3",
                    ["sql"] = "SELECT * FROM engineering_gate WHERE required = 1"
                })
        ];

        string text = DataAgentProgressDiagnosticsFormatter.Format(events, "session-a", now);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("DataAgent progress diagnostics"));
            Assert.That(text, Does.Contain("session=session-a"));
            Assert.That(text, Does.Contain("events=2"));
            Assert.That(text, Does.Contain("RouteGate:Started:Running reason=route_gate_started sql=not_executed query_allowed=true terminal=false route_allowed=true"));
            Assert.That(text, Does.Contain("Execute:Completed:Succeeded reason=read_only_query_executed sql=redacted query_allowed=true terminal=false rows=3"));
            Assert.That(text.IndexOf("RouteGate", StringComparison.Ordinal), Is.LessThan(text.IndexOf("Execute", StringComparison.Ordinal)));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("engineering_gate"));
        });
    }

    [Test]
    public void FormatRedactsUnsafeFacts()
    {
        DateTimeOffset now = Now();
        DataAgentProgressEvent unsafeEvent = new(
            "session-a",
            DataAgentProgressEventKind.Execute,
            DataAgentProgressEventPhase.Completed,
            DataAgentProgressEventStatus.Succeeded,
            "read_only_query_executed",
            1,
            now,
            ExecutedSql: true,
            QueryAllowed: true,
            Terminal: false,
            new Dictionary<string, string>
            {
                ["sql"] = "SELECT * FROM engineering_gate WHERE required = 1",
                ["hidden_context"] = "[hidden_context]secret[/hidden_context]",
                ["tool_route_context"] = "Allowed XML tools for this turn: dataagent_query",
                ["token"] = "Bearer sk-test1234567890",
                ["connection_string"] = "Host=localhost;Username=postgres;Password=secret",
                ["rows"] = "3"
            });

        string text = DataAgentProgressDiagnosticsFormatter.Format([unsafeEvent], "session-a", now);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("DataAgent progress diagnostics"));
            Assert.That(text, Does.Contain("sql=redacted"));
            Assert.That(text, Does.Contain("rows=3"));
            Assert.That(text, Does.Not.Contain("SELECT"));
            Assert.That(text, Does.Not.Contain("engineering_gate"));
            Assert.That(text, Does.Not.Contain("hidden_context"));
            Assert.That(text, Does.Not.Contain("Allowed XML tools"));
            Assert.That(text, Does.Not.Contain("sk-test"));
            Assert.That(text, Does.Not.Contain("Password=secret"));
            Assert.That(text, Does.Not.Contain("dataagent_query"));
            Assert.That(text, Does.Not.Contain("Host=localhost"));
        });
    }

    [Test]
    public void FormatBoundsOutputLength()
    {
        DateTimeOffset now = Now();
        List<DataAgentProgressEvent> events = [];
        for (int i = 0; i < 40; i++)
        {
            events.Add(Event(
                DataAgentProgressEventKind.Planner,
                DataAgentProgressEventPhase.Completed,
                DataAgentProgressEventStatus.Succeeded,
                "planner_completed_" + i,
                now.AddSeconds(i),
                executedSql: false,
                queryAllowed: true,
                terminal: false,
                new Dictionary<string, string>
                {
                    ["detail"] = new string('x', 120)
                }));
        }

        string text = DataAgentProgressDiagnosticsFormatter.Format(events, "session-a", now, maxChars: 480);

        Assert.Multiple(() =>
        {
            Assert.That(text.Length, Is.LessThanOrEqualTo(480));
            Assert.That(text, Does.EndWith("..."));
        });
    }

    static DataAgentProgressEvent Event(
        DataAgentProgressEventKind kind,
        DataAgentProgressEventPhase phase,
        DataAgentProgressEventStatus status,
        string reason,
        DateTimeOffset at,
        bool executedSql,
        bool queryAllowed,
        bool terminal,
        IReadOnlyDictionary<string, string> facts)
    {
        return new DataAgentProgressEvent(
            "session-a",
            kind,
            phase,
            status,
            reason,
            TurnCount: 1,
            at,
            executedSql,
            queryAllowed,
            terminal,
            facts);
    }

    static DateTimeOffset Now()
    {
        return new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
    }
}
