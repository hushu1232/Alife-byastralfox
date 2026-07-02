using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatRecentDiagnosticsCacheTests
{
    [Test]
    public void GetLatestReturnsNewestEntryForSessionAndKind()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "qchat_semantic_window", "old", start);
        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "qchat_semantic_window", "new", start.AddSeconds(5));

        QChatRecentDiagnosticEntry? latest = cache.GetLatest(
            "session-a",
            QChatRecentDiagnosticKind.SemanticState,
            start.AddSeconds(6));

        Assert.Multiple(() =>
        {
            Assert.That(latest, Is.Not.Null);
            Assert.That(latest!.Text, Is.EqualTo("new"));
            Assert.That(latest.Source, Is.EqualTo("qchat_semantic_window"));
            Assert.That(latest.Redacted, Is.False);
            Assert.That(latest.ReasonCode, Is.EqualTo("ok"));
        });
    }

    [Test]
    public void GetLatestUsesInsertionOrderWhenEntriesShareTimestamp()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "old_source", "old", now);
        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "new_source", "new", now);

        QChatRecentDiagnosticEntry? latest = cache.GetLatest(
            "session-a",
            QChatRecentDiagnosticKind.SemanticState,
            now);

        Assert.Multiple(() =>
        {
            Assert.That(latest, Is.Not.Null);
            Assert.That(latest!.Text, Is.EqualTo("new"));
            Assert.That(latest.Source, Is.EqualTo("new_source"));
        });
    }

    [Test]
    public void GetLatestIsolatesSessionsAndKinds()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "qchat_semantic_window", "semantic-a", now);
        cache.Record(QChatRecentDiagnosticKind.DataAgentEvidence, "session-a", "dataagent_analysis", "evidence-a", now);
        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-b", "qchat_semantic_window", "semantic-b", now);

        Assert.Multiple(() =>
        {
            Assert.That(cache.GetLatest("session-a", QChatRecentDiagnosticKind.SemanticState, now)!.Text, Is.EqualTo("semantic-a"));
            Assert.That(cache.GetLatest("session-a", QChatRecentDiagnosticKind.DataAgentEvidence, now)!.Text, Is.EqualTo("evidence-a"));
            Assert.That(cache.GetLatest("session-b", QChatRecentDiagnosticKind.SemanticState, now)!.Text, Is.EqualTo("semantic-b"));
            Assert.That(cache.GetLatest("session-b", QChatRecentDiagnosticKind.DataAgentEvidence, now), Is.Null);
        });
    }

    [Test]
    public void RecordEvictsOldestEntriesWithinSessionCapacity()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 2, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "source", "first", start);
        cache.Record(QChatRecentDiagnosticKind.DataAgentEvidence, "session-a", "source", "second", start.AddSeconds(1));
        cache.Record(QChatRecentDiagnosticKind.ToolRoute, "session-a", "source", "third", start.AddSeconds(2));

        IReadOnlyList<QChatRecentDiagnosticEntry> entries = cache.GetRecent("session-a", start.AddSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(entry => entry.Text), Is.EqualTo(new[] { "second", "third" }));
            Assert.That(cache.GetLatest("session-a", QChatRecentDiagnosticKind.SemanticState, start.AddSeconds(3)), Is.Null);
        });
    }

    [Test]
    public void RecordEvictsOnlyOldestEntryWhenDuplicateEntriesHaveEqualValues()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 2, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "source", "duplicate", now);
        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "source", "duplicate", now);
        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "source", "duplicate", now);

        IReadOnlyList<QChatRecentDiagnosticEntry> entries = cache.GetRecent("session-a", now);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries.Select(entry => entry.Text), Is.EqualTo(new[] { "duplicate", "duplicate" }));
        });
    }

    [Test]
    public void RecordIgnoresEmptySessionOrText()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        cache.Record(QChatRecentDiagnosticKind.SemanticState, "", "source", "text", now);
        cache.Record(QChatRecentDiagnosticKind.SemanticState, "   ", "source", "text", now);
        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "source", "", now);
        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "source", "   ", now);
        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "source", null, now);

        Assert.That(cache.GetRecent("session-a", now), Is.Empty);
    }

    [Test]
    public void GetRecentIgnoresExpiredEntries()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 4, ttl: TimeSpan.FromMinutes(1));
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "source", "expired", start);
        cache.Record(QChatRecentDiagnosticKind.ToolRoute, "session-a", "source", "fresh", start.AddSeconds(70));

        IReadOnlyList<QChatRecentDiagnosticEntry> entries = cache.GetRecent("session-a", start.AddSeconds(75));

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Text, Is.EqualTo("fresh"));
            Assert.That(cache.GetLatest("session-a", QChatRecentDiagnosticKind.SemanticState, start.AddSeconds(75)), Is.Null);
        });
    }

    [Test]
    public void GetRecentDoesNotPruneExpiredEntriesForOtherSessions()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 4, ttl: TimeSpan.FromSeconds(30));
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "source", "session-a-before-expiry", start);
        cache.Record(QChatRecentDiagnosticKind.ToolRoute, "session-b", "source", "session-b-fresh", start.AddSeconds(20));

        IReadOnlyList<QChatRecentDiagnosticEntry> sessionBEntries = cache.GetRecent("session-b", start.AddSeconds(40));
        IReadOnlyList<QChatRecentDiagnosticEntry> sessionAEntriesBeforeExpiry = cache.GetRecent("session-a", start.AddSeconds(20));

        Assert.Multiple(() =>
        {
            Assert.That(sessionBEntries.Select(entry => entry.Text), Is.EqualTo(new[] { "session-b-fresh" }));
            Assert.That(sessionAEntriesBeforeExpiry.Select(entry => entry.Text), Is.EqualTo(new[] { "session-a-before-expiry" }));
        });
    }

    [Test]
    public void ReadsFilterExpiredEntriesWithoutRemovingThem()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 4, ttl: TimeSpan.FromMinutes(1));
        DateTimeOffset start = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "source", "semantic-before-expiry", start);

        DateTimeOffset afterExpiry = start.AddSeconds(90);
        IReadOnlyList<QChatRecentDiagnosticEntry> laterEntries = cache.GetRecent("session-a", afterExpiry);
        QChatRecentDiagnosticEntry? laterLatest = cache.GetLatest(
            "session-a",
            QChatRecentDiagnosticKind.SemanticState,
            afterExpiry);
        IReadOnlyList<QChatRecentDiagnosticEntry> earlierEntries = cache.GetRecent("session-a", start.AddSeconds(30));
        QChatRecentDiagnosticEntry? earlierLatest = cache.GetLatest(
            "session-a",
            QChatRecentDiagnosticKind.SemanticState,
            start.AddSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(laterEntries, Is.Empty);
            Assert.That(laterLatest, Is.Null);
            Assert.That(earlierEntries.Select(entry => entry.Text), Is.EqualTo(new[] { "semantic-before-expiry" }));
            Assert.That(earlierLatest, Is.Not.Null);
            Assert.That(earlierLatest!.Text, Is.EqualTo("semantic-before-expiry"));
        });
    }

    [Test]
    public void RecordCapsLongDiagnosticTextAtNineHundredCharacters()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");
        string longText = new('a', 1_000);

        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "source", longText, now);

        QChatRecentDiagnosticEntry latest = cache.GetLatest("session-a", QChatRecentDiagnosticKind.SemanticState, now)!;

        Assert.Multiple(() =>
        {
            Assert.That(latest.Text, Has.Length.EqualTo(900));
            Assert.That(latest.Text, Does.EndWith("..."));
            Assert.That(latest.Redacted, Is.False);
        });
    }

    [TestCase("[tool_route_context]\nAllowed XML tools: dataagent_query\n[/tool_route_context]")]
    [TestCase("[data_agent_evidence_pack]\nanalysis_confidence=0.9\n[/data_agent_evidence_pack]")]
    [TestCase("connection_string=Host=localhost;Username=test")]
    [TestCase("api_key=sk-test")]
    [TestCase("Host=postgres.example;Username=alife;Password=secret")]
    [TestCase("Authorization: Bearer token-abcdef123456")]
    [TestCase("api-key=secret")]
    [TestCase("apiKey=secret")]
    [TestCase("SELECT * FROM users")]
    [TestCase("SELECT*FROM users")]
    [TestCase("SELECT\n* FROM users")]
    [TestCase("DELETE\nFROM query_audit")]
    [TestCase("DROP TABLE users")]
    [TestCase("CREATE TABLE secrets(id int)")]
    [TestCase("ALTER TABLE users ADD COLUMN secret text")]
    [TestCase("TRUNCATE TABLE audit_log")]
    [TestCase("MERGE INTO users USING source")]
    [TestCase("EXEC sp_read_secret")]
    public void RecordRedactsUnsafeDiagnosticText(string unsafeText)
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 4, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:00:00Z");

        cache.Record(QChatRecentDiagnosticKind.DataAgentEvidence, "session-a", "dataagent_analysis", unsafeText, now);

        QChatRecentDiagnosticEntry latest = cache.GetLatest("session-a", QChatRecentDiagnosticKind.DataAgentEvidence, now)!;
        Assert.Multiple(() =>
        {
            Assert.That(latest.Redacted, Is.True);
            Assert.That(latest.ReasonCode, Is.EqualTo("hidden_context_redacted"));
            Assert.That(latest.Text, Does.Contain("DataAgent evidence diagnostics"));
            Assert.That(latest.Text, Does.Contain("state=redacted"));
            Assert.That(latest.Text, Does.Not.Contain("[tool_route_context]"));
            Assert.That(latest.Text, Does.Not.Contain("Allowed XML tools"));
            Assert.That(latest.Text, Does.Not.Contain("sk-test"));
            Assert.That(latest.Text, Does.Not.Contain("SELECT"));
            Assert.That(latest.Text, Does.Not.Contain("secret"));
            Assert.That(latest.Text, Does.Not.Contain("token-abcdef123456"));
            Assert.That(latest.Text, Does.Not.Contain("DROP TABLE"));
        });
    }

    [Test]
    public void FormatSummaryEmitsStableRecentDiagnosticsLines()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 8, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        cache.Record(QChatRecentDiagnosticKind.SemanticState, "session-a", "qchat_semantic_window", "QChat semantic diagnostics", now.AddSeconds(-3));
        cache.Record(QChatRecentDiagnosticKind.DataAgentEvidence, "session-a", "dataagent_analysis", "DataAgent evidence diagnostics", now.AddSeconds(-12));
        cache.Record(QChatRecentDiagnosticKind.ToolRoute, "session-a", "tool_broker", "allowed=dataagent_analysis_start", now.AddSeconds(-2));

        string text = QChatRecentDiagnosticsFormatter.FormatSummary(
            cache.GetRecent("session-a", now),
            "session-a",
            now);

        string[] expectedLines =
        [
            "QChat recent diagnostics",
            "semantic_state_recent=available age_seconds=3 source=qchat_semantic_window redacted=false",
            "dataagent_evidence_recent=available age_seconds=12 source=dataagent_analysis redacted=false",
            "tool_route_recent=available age_seconds=2 source=tool_broker redacted=false",
            "session=session-a"
        ];
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }

    [Test]
    public void FormatSummaryUsesInsertionOrderWhenEntriesShareTimestamp()
    {
        QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 8, ttl: TimeSpan.FromMinutes(30));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
        cache.Record(QChatRecentDiagnosticKind.ToolRoute, "session-a", "old_source", "allowed=old", now.AddSeconds(-2));
        cache.Record(QChatRecentDiagnosticKind.ToolRoute, "session-a", "new_source", "api_key=sk-test", now.AddSeconds(-2));

        string text = QChatRecentDiagnosticsFormatter.FormatSummary(
            cache.GetRecent("session-a", now),
            "session-a",
            now);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("tool_route_recent=available age_seconds=2 source=new_source redacted=true"));
            Assert.That(text, Does.Not.Contain("source=old_source"));
        });
    }

    [Test]
    public void FormatSummaryEmitsUnavailableWhenEmpty()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");

        string text = QChatRecentDiagnosticsFormatter.FormatSummary([], "session-a", now);

        string[] expectedLines =
        [
            "QChat recent diagnostics",
            "state=unavailable",
            "reason=recent_diagnostics_empty",
            "session=session-a"
        ];
        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(expectedLines));
    }
}
