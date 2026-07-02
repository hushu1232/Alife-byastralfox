# DataAgent V2.7 Recent Diagnostics Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a session-scoped recent diagnostics cache for QChat semantic state, DataAgent evidence diagnostics, and Tool Broker route traces while keeping diagnostics read-only and owner-only.

**Architecture:** Keep QChat as the owner of the in-memory recent diagnostics cache. DataAgent continues to produce safe evidence diagnostics strings, FunctionCaller keeps its safe bridge, and Tool Broker remains the only permission authority. `QChatDiagnosticsService` reads cache entries when a session key is available and falls back to the existing V2.6 recent-string behavior when no cache entry exists.

**Tech Stack:** C#/.NET 9, NUnit, existing QChat diagnostics commands, existing DataAgent Evidence Pack diagnostics, existing Tool Broker route trace, PowerShell readiness scripts.

---

## Execution Setup

Create an isolated worktree before implementation:

```powershell
git worktree add "D:\Alife\.worktrees\dataagent-v2.7-recent-diagnostics-cache" -b dataagent-v2.7-recent-diagnostics-cache
```

Run implementation commands from:

```text
D:\Alife\.worktrees\dataagent-v2.7-recent-diagnostics-cache
```

Use the local .NET 9 SDK:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe"
```

Do not use `D:\FOXD`. Push only to:

```text
git@github.com:hushu1232/Alife-byastralfox.git
```

## File Structure

- Create `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs`
  - Owns the in-memory session-scoped cache, cache entry model, kind enum, TTL, capacity, normalization, and redaction.

- Create `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs`
  - Formats `/qchat diag recent` summaries and renders a single cached entry for diagnostics commands.

- Create `Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs`
  - Tests latest entry lookup, session isolation, capacity eviction, TTL expiry, redaction, and summary formatting.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
  - Extends `QChatDiagnosticsRuntimeState` with `RecentDiagnosticsCache`, `SessionKey`, and `DiagnosticsNow`.
  - Adds `/qchat diag recent`.
  - Reads cache entries before falling back to legacy recent strings.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`
  - Accepts an optional cache instance and passes route session key into `QChatDiagnosticsRuntimeState`.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Creates one `QChatRecentDiagnosticsCache`.
  - Records semantic diagnostics, DataAgent evidence diagnostics, and Tool Broker route traces by QChat route session key.
  - Keeps existing recent string fields as compatibility fallbacks.

- Modify `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`
  - Tests `/qchat diag recent` and cache-first behavior for semantic, evidence, and Tool Broker diagnostics.

- Modify `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`
  - Tests owner command service passes cache and session key.

- Modify `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
  - Tests live adapter behavior for recent cache reads and session isolation.

- Modify `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
  - Adds a DataAgent-side bridge readiness check proving evidence diagnostics remain safe before QChat caches them.

- Modify `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
  - Requires the new DataAgent bridge readiness check and updated readiness count.

- Modify `tools/check-dataagent-readiness.ps1`
  - Adds a required marker check for the V2.7 evidence diagnostics bridge.

- Modify `tools/check-qchat-engineering-map.ps1`
  - Adds required markers for the V2.7 recent diagnostics cache and `/qchat diag recent`.

- Modify `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`
  - Requires the V2.7 QChat engineering map checks.

---

### Task 1: Recent Diagnostics Cache And Formatter

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs`
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs`
- Create: `Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs`

- [ ] **Step 1: Write failing cache tests**

Create `Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs`:

```csharp
using Alife.Function.QChat;

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

    [TestCase("[tool_route_context]\nAllowed XML tools: dataagent_query\n[/tool_route_context]")]
    [TestCase("[data_agent_evidence_pack]\nanalysis_confidence=0.9\n[/data_agent_evidence_pack]")]
    [TestCase("connection_string=Host=localhost;Username=test")]
    [TestCase("api_key=sk-test")]
    [TestCase("SELECT * FROM users")]
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
```

- [ ] **Step 2: Run failing cache tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatRecentDiagnosticsCacheTests" -v:minimal
```

Expected: FAIL with compiler errors because `QChatRecentDiagnosticsCache`, `QChatRecentDiagnosticKind`, `QChatRecentDiagnosticEntry`, and `QChatRecentDiagnosticsFormatter` do not exist.

- [ ] **Step 3: Add cache implementation**

Create `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Alife.Function.QChat;

public enum QChatRecentDiagnosticKind
{
    SemanticState,
    DataAgentEvidence,
    ToolRoute
}

public sealed record QChatRecentDiagnosticEntry(
    QChatRecentDiagnosticKind Kind,
    string SessionKey,
    string Source,
    string Text,
    DateTimeOffset CreatedAt,
    bool Redacted,
    string ReasonCode);

public sealed class QChatRecentDiagnosticsCache
{
    const int MaxTextChars = 900;

    readonly object gate = new();
    readonly int maxEntriesPerSession;
    readonly TimeSpan ttl;
    readonly List<QChatRecentDiagnosticEntry> entries = [];

    public QChatRecentDiagnosticsCache(int maxEntriesPerSession = 12, TimeSpan? ttl = null)
    {
        this.maxEntriesPerSession = Math.Max(1, maxEntriesPerSession);
        this.ttl = ttl ?? TimeSpan.FromMinutes(30);
    }

    public void Record(
        QChatRecentDiagnosticKind kind,
        string sessionKey,
        string source,
        string? text,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(sessionKey) || string.IsNullOrWhiteSpace(text))
            return;

        string normalizedSessionKey = NormalizeToken(sessionKey);
        string normalizedSource = string.IsNullOrWhiteSpace(source) ? "unknown" : NormalizeToken(source);
        QChatRecentDiagnosticEntry entry = CreateEntry(kind, normalizedSessionKey, normalizedSource, text, createdAt);

        lock (gate)
        {
            PruneExpiredLocked(createdAt);
            entries.Add(entry);
            PruneCapacityLocked(normalizedSessionKey);
        }
    }

    public QChatRecentDiagnosticEntry? GetLatest(
        string sessionKey,
        QChatRecentDiagnosticKind kind,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            return null;

        string normalizedSessionKey = NormalizeToken(sessionKey);
        lock (gate)
        {
            PruneExpiredLocked(now);
            return entries
                .Where(entry => entry.Kind == kind && string.Equals(entry.SessionKey, normalizedSessionKey, StringComparison.Ordinal))
                .OrderByDescending(entry => entry.CreatedAt)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<QChatRecentDiagnosticEntry> GetRecent(string sessionKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            return [];

        string normalizedSessionKey = NormalizeToken(sessionKey);
        lock (gate)
        {
            PruneExpiredLocked(now);
            return entries
                .Where(entry => string.Equals(entry.SessionKey, normalizedSessionKey, StringComparison.Ordinal))
                .OrderBy(entry => entry.CreatedAt)
                .ToArray();
        }
    }

    QChatRecentDiagnosticEntry CreateEntry(
        QChatRecentDiagnosticKind kind,
        string sessionKey,
        string source,
        string text,
        DateTimeOffset createdAt)
    {
        if (ContainsUnsafeDiagnosticText(text))
        {
            return new QChatRecentDiagnosticEntry(
                kind,
                sessionKey,
                source,
                QChatRecentDiagnosticsFormatter.FormatRedacted(kind),
                createdAt,
                Redacted: true,
                ReasonCode: "hidden_context_redacted");
        }

        return new QChatRecentDiagnosticEntry(
            kind,
            sessionKey,
            source,
            NormalizeDiagnosticText(text),
            createdAt,
            Redacted: false,
            ReasonCode: "ok");
    }

    void PruneExpiredLocked(DateTimeOffset now)
    {
        entries.RemoveAll(entry => now - entry.CreatedAt > ttl);
    }

    void PruneCapacityLocked(string sessionKey)
    {
        List<QChatRecentDiagnosticEntry> sessionEntries = entries
            .Where(entry => string.Equals(entry.SessionKey, sessionKey, StringComparison.Ordinal))
            .OrderBy(entry => entry.CreatedAt)
            .ToList();

        int excess = sessionEntries.Count - maxEntriesPerSession;
        if (excess <= 0)
            return;

        HashSet<QChatRecentDiagnosticEntry> toRemove = sessionEntries.Take(excess).ToHashSet();
        entries.RemoveAll(toRemove.Contains);
    }

    static string NormalizeDiagnosticText(string text)
    {
        string normalized = text.ReplaceLineEndings(Environment.NewLine).Trim();
        return normalized.Length <= MaxTextChars
            ? normalized
            : normalized[..MaxTextChars] + "...";
    }

    static string NormalizeToken(string value)
    {
        return value.ReplaceLineEndings(" ").Replace(';', ',').Trim();
    }

    static bool ContainsUnsafeDiagnosticText(string text)
    {
        return text.Contains("[tool_route_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[/tool_route_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[data_agent_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[/data_agent_context]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[data_agent_evidence_pack]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("[/data_agent_evidence_pack]", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Allowed XML tools", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("connection_string", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("sk-", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("SELECT ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("INSERT ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("DELETE ", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Add formatter implementation**

Create `sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Alife.Function.QChat;

public static class QChatRecentDiagnosticsFormatter
{
    public static string FormatSummary(
        IReadOnlyList<QChatRecentDiagnosticEntry> entries,
        string sessionKey,
        DateTimeOffset now)
    {
        if (entries.Count == 0)
            return string.Join(Environment.NewLine,
                "QChat recent diagnostics",
                "state=unavailable",
                "reason=recent_diagnostics_empty",
                "session=" + NormalizeToken(sessionKey));

        return string.Join(Environment.NewLine,
            "QChat recent diagnostics",
            FormatKindLine("semantic_state_recent", entries, QChatRecentDiagnosticKind.SemanticState, now),
            FormatKindLine("dataagent_evidence_recent", entries, QChatRecentDiagnosticKind.DataAgentEvidence, now),
            FormatKindLine("tool_route_recent", entries, QChatRecentDiagnosticKind.ToolRoute, now),
            "session=" + NormalizeToken(sessionKey));
    }

    public static string FormatRedacted(QChatRecentDiagnosticKind kind)
    {
        return string.Join(Environment.NewLine,
            Title(kind),
            "state=redacted",
            "reason=hidden_context_redacted");
    }

    public static string Title(QChatRecentDiagnosticKind kind)
    {
        return kind switch
        {
            QChatRecentDiagnosticKind.SemanticState => "QChat semantic diagnostics",
            QChatRecentDiagnosticKind.DataAgentEvidence => "DataAgent evidence diagnostics",
            QChatRecentDiagnosticKind.ToolRoute => "Tool Broker diagnostics",
            _ => "QChat diagnostics"
        };
    }

    static string FormatKindLine(
        string label,
        IReadOnlyList<QChatRecentDiagnosticEntry> entries,
        QChatRecentDiagnosticKind kind,
        DateTimeOffset now)
    {
        QChatRecentDiagnosticEntry? latest = entries
            .Where(entry => entry.Kind == kind)
            .OrderByDescending(entry => entry.CreatedAt)
            .FirstOrDefault();

        if (latest is null)
            return label + "=missing";

        double ageSeconds = Math.Max(0, (now - latest.CreatedAt).TotalSeconds);
        return string.Join(' ',
            label + "=available",
            "age_seconds=" + ageSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            "source=" + NormalizeToken(latest.Source),
            "redacted=" + (latest.Redacted ? "true" : "false"));
    }

    static string NormalizeToken(string value)
    {
        return string.Join(' ', (value ?? string.Empty)
            .ReplaceLineEndings(" ")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
```

- [ ] **Step 5: Run cache tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatRecentDiagnosticsCacheTests" -v:minimal
```

Expected: PASS for all `QChatRecentDiagnosticsCacheTests`.

- [ ] **Step 6: Commit Task 1**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsFormatter.cs Tests/Alife.Test.QChat/QChatRecentDiagnosticsCacheTests.cs
git commit -m "Add QChat recent diagnostics cache"
```

---

### Task 2: QChat Diagnostics Service Cache Reads

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs`

- [ ] **Step 1: Add failing diagnostics service tests**

Append these tests to `Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs` before the null route/profile tests:

```csharp
[Test]
public void TryHandleRecentDiagnosticsReturnsSessionCacheSummary()
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
    QChatRecentDiagnosticsCache cache = new(maxEntriesPerSession: 8, ttl: TimeSpan.FromMinutes(30));
    cache.Record(QChatRecentDiagnosticKind.SemanticState, "qq:xiayu:2905391496:private:3045846738", "qchat_semantic_window", "QChat semantic diagnostics", now.AddSeconds(-3));
    cache.Record(QChatRecentDiagnosticKind.DataAgentEvidence, "qq:xiayu:2905391496:private:3045846738", "dataagent_analysis", "DataAgent evidence diagnostics", now.AddSeconds(-12));
    cache.Record(QChatRecentDiagnosticKind.ToolRoute, "qq:xiayu:2905391496:private:3045846738", "tool_broker", "allowed=dataagent_analysis_start", now.AddSeconds(-2));

    QChatDiagnosticsRuntimeState state = new(
        RecentDiagnosticsCache: cache,
        SessionKey: "qq:xiayu:2905391496:private:3045846738",
        DiagnosticsNow: now);

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/qchat diag recent",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("QChat recent diagnostics"));
        Assert.That(result.Text, Does.Contain("semantic_state_recent=available age_seconds=3"));
        Assert.That(result.Text, Does.Contain("dataagent_evidence_recent=available age_seconds=12"));
        Assert.That(result.Text, Does.Contain("tool_route_recent=available age_seconds=2"));
        Assert.That(result.Text, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
    });
}

[Test]
public void TryHandleRecentDiagnosticsReturnsUnavailableWhenSessionCacheIsEmpty()
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
    QChatDiagnosticsRuntimeState state = new(
        RecentDiagnosticsCache: new QChatRecentDiagnosticsCache(),
        SessionKey: "qq:xiayu:2905391496:private:3045846738",
        DiagnosticsNow: now);

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/qchat diag recent",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Handled, Is.True);
        Assert.That(result.Text, Does.Contain("QChat recent diagnostics"));
        Assert.That(result.Text, Does.Contain("state=unavailable"));
        Assert.That(result.Text, Does.Contain("reason=recent_diagnostics_empty"));
    });
}

[Test]
public void TryHandleSemanticDiagnosticsPrefersSessionCacheOverLegacyRecentString()
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
    QChatRecentDiagnosticsCache cache = new();
    cache.Record(
        QChatRecentDiagnosticKind.SemanticState,
        "qq:xiayu:2905391496:private:3045846738",
        "qchat_semantic_window",
        string.Join(Environment.NewLine,
            "QChat semantic diagnostics",
            "semantic_completion=0.901",
            "reason_code=from_cache"),
        now);
    QChatDiagnosticsRuntimeState state = new(
        RecentSemanticEstimate: "legacy semantic text",
        RecentDiagnosticsCache: cache,
        SessionKey: "qq:xiayu:2905391496:private:3045846738",
        DiagnosticsNow: now);

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/qchat diag semantic",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Text, Does.Contain("semantic_completion=0.901"));
        Assert.That(result.Text, Does.Contain("reason_code=from_cache"));
        Assert.That(result.Text, Does.Not.Contain("legacy semantic text"));
    });
}

[Test]
public void TryHandleDataAgentEvidenceDiagnosticsPrefersSessionCacheOverLegacyRecentString()
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
    QChatRecentDiagnosticsCache cache = new();
    cache.Record(
        QChatRecentDiagnosticKind.DataAgentEvidence,
        "qq:xiayu:2905391496:private:3045846738",
        "dataagent_analysis",
        string.Join(Environment.NewLine,
            "DataAgent evidence diagnostics",
            "analysis_confidence=0.912",
            "risk_level=0.101"),
        now);
    QChatDiagnosticsRuntimeState state = new(
        RecentDataAgentEvidence: "legacy evidence text",
        RecentDiagnosticsCache: cache,
        SessionKey: "qq:xiayu:2905391496:private:3045846738",
        DiagnosticsNow: now);

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/dataagent diag evidence",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Text, Does.Contain("analysis_confidence=0.912"));
        Assert.That(result.Text, Does.Contain("risk_level=0.101"));
        Assert.That(result.Text, Does.Not.Contain("legacy evidence text"));
    });
}

[Test]
public void TryHandleToolBrokerDiagnosticsPrefersSessionCacheOverLegacyTrace()
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
    QChatRecentDiagnosticsCache cache = new();
    cache.Record(
        QChatRecentDiagnosticKind.ToolRoute,
        "qq:xiayu:2905391496:private:3045846738",
        "tool_broker",
        string.Join(Environment.NewLine,
            "Tool Broker diagnostics",
            "recent=allowed=dataagent_analysis_start; denied=none; reason=route_allowed"),
        now);
    QChatDiagnosticsRuntimeState state = new(
        RecentToolRouteTrace: "legacy-route-trace",
        RecentDiagnosticsCache: cache,
        SessionKey: "qq:xiayu:2905391496:private:3045846738",
        DiagnosticsNow: now);

    QChatDiagnosticsResult result = QChatDiagnosticsService.TryHandle(
        "/qchat diag toolbroker",
        CreateRoute(),
        CreateProfile(),
        state);

    Assert.Multiple(() =>
    {
        Assert.That(result.Text, Does.Contain("allowed=dataagent_analysis_start"));
        Assert.That(result.Text, Does.Not.Contain("legacy-route-trace"));
    });
}
```

- [ ] **Step 2: Run failing diagnostics service tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "Name~RecentDiagnostics|Name~PrefersSessionCache" -v:minimal
```

Expected: FAIL because `QChatDiagnosticsRuntimeState` has no cache fields and `/qchat diag recent` is not handled.

- [ ] **Step 3: Extend runtime state**

In `sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs`, replace the `QChatDiagnosticsRuntimeState` record with:

```csharp
public sealed record QChatDiagnosticsRuntimeState(
    bool ReplyTimingDelayEnabled = false,
    bool ConversationSettleWindowEnabled = false,
    bool InternetAccessEnabled = false,
    string? RecentToolRouteTrace = null,
    string? RecentSemanticEstimate = null,
    string? RecentDataAgentEvidence = null,
    QChatRecentDiagnosticsCache? RecentDiagnosticsCache = null,
    string? SessionKey = null,
    DateTimeOffset? DiagnosticsNow = null);
```

- [ ] **Step 4: Add `/qchat diag recent` and cache-first reads**

In `QChatDiagnosticsService.TryHandle`, add this switch case before `"diag toolbroker"`:

```csharp
"diag recent" or "diagnostics recent" => Handled(BuildRecentDiagnosticsText(runtimeState, route)),
```

Replace `BuildToolBrokerText`, `BuildSemanticDiagnosticsText`, and `BuildDataAgentEvidenceDiagnosticsText` with:

```csharp
static string BuildRecentDiagnosticsText(QChatDiagnosticsRuntimeState runtimeState, QChatAgentRoute route)
{
    string sessionKey = GetDiagnosticsSessionKey(runtimeState, route);
    DateTimeOffset now = runtimeState.DiagnosticsNow ?? DateTimeOffset.UtcNow;
    IReadOnlyList<QChatRecentDiagnosticEntry> entries = runtimeState.RecentDiagnosticsCache?.GetRecent(sessionKey, now) ?? [];
    return QChatRecentDiagnosticsFormatter.FormatSummary(entries, sessionKey, now);
}

static string BuildToolBrokerText(QChatDiagnosticsRuntimeState runtimeState)
{
    string? cached = GetRecentCachedText(runtimeState, QChatRecentDiagnosticKind.ToolRoute);
    if (string.IsNullOrWhiteSpace(cached) == false)
        return cached;

    string trace = SanitizeToolRouteTrace(runtimeState.RecentToolRouteTrace);
    return string.Join(Environment.NewLine,
        "Tool Broker diagnostics",
        $"recent={trace}");
}

static string BuildSemanticDiagnosticsText(QChatDiagnosticsRuntimeState runtimeState)
{
    string? cached = GetRecentCachedText(runtimeState, QChatRecentDiagnosticKind.SemanticState);
    if (string.IsNullOrWhiteSpace(cached) == false)
        return cached;

    string sanitized = SanitizeDiagnosticText(
        runtimeState.RecentSemanticEstimate,
        "QChat semantic diagnostics");
    return string.IsNullOrWhiteSpace(sanitized)
        ? QChatSemanticDiagnosticsFormatter.Format(new QChatSemanticDiagnosticsSnapshot(null, 0, TimeSpan.Zero, TimeSpan.Zero))
        : sanitized;
}

static string BuildDataAgentEvidenceDiagnosticsText(QChatDiagnosticsRuntimeState runtimeState)
{
    string? cached = GetRecentCachedText(runtimeState, QChatRecentDiagnosticKind.DataAgentEvidence);
    if (string.IsNullOrWhiteSpace(cached) == false)
        return cached;

    string sanitized = SanitizeDiagnosticText(
        runtimeState.RecentDataAgentEvidence,
        "DataAgent evidence diagnostics");
    return string.IsNullOrWhiteSpace(sanitized)
        ? string.Join(Environment.NewLine,
            "DataAgent evidence diagnostics",
            "state=unavailable",
            "reason=evidence_pack_unavailable")
        : sanitized;
}
```

Add these helpers after `BuildDataAgentEvidenceDiagnosticsText`:

```csharp
static string? GetRecentCachedText(QChatDiagnosticsRuntimeState runtimeState, QChatRecentDiagnosticKind kind)
{
    if (runtimeState.RecentDiagnosticsCache is null || string.IsNullOrWhiteSpace(runtimeState.SessionKey))
        return null;

    DateTimeOffset now = runtimeState.DiagnosticsNow ?? DateTimeOffset.UtcNow;
    return runtimeState.RecentDiagnosticsCache.GetLatest(runtimeState.SessionKey, kind, now)?.Text;
}

static string GetDiagnosticsSessionKey(QChatDiagnosticsRuntimeState runtimeState, QChatAgentRoute route)
{
    return string.IsNullOrWhiteSpace(runtimeState.SessionKey)
        ? route.SessionKey
        : runtimeState.SessionKey;
}
```

Update `BuildDiagnosticsMenuText` so it includes:

```csharp
"/qchat diag recent - Recent diagnostics cache summary",
```

Place it before `/qchat diag semantic`.

- [ ] **Step 5: Run diagnostics service tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatRecentDiagnosticsCacheTests" -v:minimal
```

Expected: PASS for diagnostics service and cache tests.

- [ ] **Step 6: Commit Task 2**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs Tests/Alife.Test.QChat/QChatDiagnosticsServiceTests.cs
git commit -m "Read owner diagnostics from recent cache"
```

---

### Task 3: Owner Command Service Runtime State Wiring

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`

- [ ] **Step 1: Add failing owner command cache test**

Add this test to `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`:

```csharp
[Test]
public async Task TryHandleDiagnosticsCommandAsyncPassesRecentDiagnosticsCacheAndRouteSession()
{
    FakeOneBotMessageSink sent = new();
    DateTimeOffset now = DateTimeOffset.Parse("2026-07-02T00:01:00Z");
    QChatRecentDiagnosticsCache cache = new();
    cache.Record(
        QChatRecentDiagnosticKind.ToolRoute,
        "qq:xiayu:2905391496:private:3045846738",
        "tool_broker",
        string.Join(Environment.NewLine,
            "Tool Broker diagnostics",
            "recent=allowed=dataagent_analysis_start; denied=none; reason=route_allowed"),
        now.AddSeconds(-2));

    bool handled = await QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync(
        new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/qchat diag recent"
        },
        QChatSenderRole.Owner,
        new QChatConfig
        {
            BotId = 2905391496,
            OwnerId = 3045846738
        },
        sent.SendAsync,
        (_, _, _, _) => { },
        recentDiagnosticsCache: cache,
        diagnosticsNow: () => now);

    Assert.Multiple(() =>
    {
        Assert.That(handled, Is.True);
        Assert.That(sent.Messages, Has.Count.EqualTo(1));
        Assert.That(sent.Messages[0].Message, Does.Contain("QChat recent diagnostics"));
        Assert.That(sent.Messages[0].Message, Does.Contain("tool_route_recent=available age_seconds=2"));
        Assert.That(sent.Messages[0].Message, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
    });
}
```

If this test file does not already have a `FakeOneBotMessageSink`, add this helper near the bottom of the file:

```csharp
sealed class FakeOneBotMessageSink
{
    public List<(OneBotMessageType Type, long TargetId, string Message)> Messages { get; } = [];

    public Task SendAsync(OneBotMessageType type, long targetId, string message)
    {
        Messages.Add((type, targetId, message));
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Run failing owner command test**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "Name=TryHandleDiagnosticsCommandAsyncPassesRecentDiagnosticsCacheAndRouteSession" -v:minimal
```

Expected: FAIL because `TryHandleDiagnosticsCommandAsync` does not accept `recentDiagnosticsCache` or `diagnosticsNow`.

- [ ] **Step 3: Extend owner command service parameters**

In `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`, update the `TryHandleDiagnosticsCommandAsync` signature:

```csharp
public static async Task<bool> TryHandleDiagnosticsCommandAsync(
    OneBotMessageEvent messageEvent,
    QChatSenderRole senderRole,
    QChatConfig config,
    Func<OneBotMessageType, long, string, Task> sendAsync,
    Action<string, string, object?, Exception?> writeDiagnostic,
    Func<string>? recentToolRouteTrace = null,
    Func<string>? recentSemanticEstimate = null,
    Func<string>? recentDataAgentEvidence = null,
    QChatRecentDiagnosticsCache? recentDiagnosticsCache = null,
    Func<DateTimeOffset>? diagnosticsNow = null)
```

When constructing `QChatDiagnosticsRuntimeState`, add:

```csharp
RecentDiagnosticsCache: recentDiagnosticsCache,
SessionKey: route.SessionKey,
DiagnosticsNow: diagnosticsNow?.Invoke()
```

The full runtime state construction should become:

```csharp
new QChatDiagnosticsRuntimeState(
    ReplyTimingDelayEnabled: config.EnableReplyTimingDelay,
    ConversationSettleWindowEnabled: config.EnableConversationSettleWindow,
    InternetAccessEnabled: config.EnableInternetAccess,
    RecentToolRouteTrace: recentToolRouteTrace?.Invoke(),
    RecentSemanticEstimate: recentSemanticEstimate?.Invoke(),
    RecentDataAgentEvidence: recentDataAgentEvidence?.Invoke(),
    RecentDiagnosticsCache: recentDiagnosticsCache,
    SessionKey: route.SessionKey,
    DiagnosticsNow: diagnosticsNow?.Invoke())
```

- [ ] **Step 4: Run owner command tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatDiagnosticsServiceTests" -v:minimal
```

Expected: PASS for owner command and diagnostics service tests.

- [ ] **Step 5: Commit Task 3**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs
git commit -m "Pass recent diagnostics cache to owner commands"
```

---

### Task 4: QChat Runtime Recording

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Add failing QChat runtime cache tests**

Add these tests to `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs` near the existing owner diagnostics tests:

```csharp
[Test]
public async Task OwnerPrivateQChatRecentDiagnosticsShowsRecordedDataAgentEvidence()
{
    FakeOneBotRuntime runtime = new()
    {
        BotId = 2905391496
    };
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 2905391496,
        OwnerId = 3045846738,
        EnableBalancedTextStreaming = false
    });

    service.RecordRecentDataAgentEvidenceDiagnostics(string.Join(Environment.NewLine,
        "DataAgent evidence diagnostics",
        "analysis_confidence=0.781",
        "risk_level=0.287"));

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 2905391496,
        UserId = 3045846738,
        RawMessage = "/qchat diag recent"
    });

    await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
    string reply = runtime.PrivateMessages.Single().Message;
    Assert.Multiple(() =>
    {
        Assert.That(reply, Does.Contain("QChat recent diagnostics"));
        Assert.That(reply, Does.Contain("dataagent_evidence_recent=available"));
        Assert.That(reply, Does.Contain("source=dataagent_analysis"));
        Assert.That(reply, Does.Contain("session=qq:xiayu:2905391496:private:3045846738"));
    });
}

[Test]
public async Task OwnerPrivateQChatRecentDiagnosticsShowsSemanticAndToolRouteEntries()
{
    await WithIsolatedQChatDiagnosticsAsync(async storageRoot =>
    {
        FakeOneBotRuntime runtime = new()
        {
            BotId = 2905391496
        };
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 2905391496,
            OwnerId = 3045846738,
            EnableBalancedTextStreaming = false,
            EnableConversationSettleWindow = true,
            PrivateSettleMilliseconds = 5000,
            RecallGraceMilliseconds = 1,
            MaxSettleMilliseconds = 6000
        });
        service.InboundChatDispatcher = _ => Task.CompletedTask;

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            MessageId = 7201,
            UserId = 3045846738,
            RawMessage = "how should we test v2.7?"
        });
        await WaitForQChatDiagnosticEventAsync(storageRoot, "qchat-settle-dispatch-scheduled");

        runtime.Raise(new OneBotMessageEvent
        {
            SelfId = 2905391496,
            UserId = 3045846738,
            RawMessage = "/qchat diag recent"
        });

        await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
        string reply = runtime.PrivateMessages.Single().Message;
        Assert.Multiple(() =>
        {
            Assert.That(reply, Does.Contain("QChat recent diagnostics"));
            Assert.That(reply, Does.Contain("semantic_state_recent=available"));
            Assert.That(reply, Does.Contain("source=qchat_semantic_window"));
            Assert.That(reply, Does.Not.Contain("state=unavailable"));
        });
    });
}

[Test]
public async Task OwnerPrivateDataAgentEvidenceDiagnosticsStillFallsBackToFunctionCallerBridge()
{
    FakeOneBotRuntime runtime = new()
    {
        BotId = 2905391496
    };
    XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
    functionCaller.RecordRecentDataAgentEvidenceDiagnostics(string.Join(Environment.NewLine,
        "DataAgent evidence diagnostics",
        "analysis_confidence=0.812",
        "risk_level=0.188",
        "route_allowed=true"));
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 2905391496,
        OwnerId = 3045846738,
        EnableBalancedTextStreaming = false
    }, functionCaller: functionCaller);

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 2905391496,
        UserId = 3045846738,
        RawMessage = "/dataagent diag evidence"
    });

    await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);
    string reply = runtime.PrivateMessages.Single().Message;
    Assert.Multiple(() =>
    {
        Assert.That(reply, Does.Contain("DataAgent evidence diagnostics"));
        Assert.That(reply, Does.Contain("analysis_confidence=0.812"));
        Assert.That(reply, Does.Contain("route_allowed=true"));
    });
}
```

- [ ] **Step 2: Run failing runtime cache tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "Name~RecentDiagnosticsShows|Name=OwnerPrivateDataAgentEvidenceDiagnosticsStillFallsBackToFunctionCallerBridge" -v:minimal
```

Expected: FAIL until `QChatService` owns and records into `QChatRecentDiagnosticsCache`.

- [ ] **Step 3: Add cache field**

In `QChatService`, near the existing diagnostics gates and recent strings, add:

```csharp
readonly QChatRecentDiagnosticsCache recentDiagnosticsCache = new();
```

Keep the existing fields:

```csharp
string recentToolRouteTrace = "none";
string recentSemanticDiagnostics = CreateUnavailableSemanticDiagnosticsText();
string recentDataAgentEvidenceDiagnostics = string.Empty;
```

They remain compatibility fallbacks for V2.6 behavior and FunctionCaller bridge behavior.

- [ ] **Step 4: Pass cache into owner command service**

In `TryHandleQChatDiagnosticsCommandAsync`, update the `QChatOwnerCommandService.TryHandleDiagnosticsCommandAsync` call so the last arguments are:

```csharp
GetRecentToolRouteTrace,
GetRecentSemanticDiagnostics,
GetRecentDataAgentEvidenceDiagnostics,
recentDiagnosticsCache);
```

- [ ] **Step 5: Add route-session helpers**

Add these helpers near `BuildPendingDispatchSessionKey` or near other diagnostics helpers:

```csharp
string BuildRecentDiagnosticsSessionKey(QChatInboundMessage message)
{
    long botAccountId = message.ResolvedBotId > 0
        ? message.ResolvedBotId
        : Math.Max(0, Configuration?.BotId ?? 0);
    string agentId = QChatAgentIdentityRegistry.CreateDefault().ResolveByBotId(botAccountId)?.AgentId ?? $"qq-{botAccountId}";
    string kindSegment = message.MessageType == OneBotMessageType.Group ? "group" : "private";
    long peerId = message.MessageType == OneBotMessageType.Group ? message.TargetId : message.SenderId;
    return $"qq:{agentId}:{botAccountId}:{kindSegment}:{peerId}";
}

string BuildRecentDiagnosticsSessionKey(QChatReplySession session)
{
    long botAccountId = session.ResolvedBotId > 0
        ? session.ResolvedBotId
        : Math.Max(0, Configuration?.BotId ?? 0);
    string agentId = QChatAgentIdentityRegistry.CreateDefault().ResolveByBotId(botAccountId)?.AgentId ?? $"qq-{botAccountId}";
    string kindSegment = session.MessageType == OneBotMessageType.Group ? "group" : "private";
    long peerId = session.MessageType == OneBotMessageType.Group ? session.TargetId : session.SenderId;
    return $"qq:{agentId}:{botAccountId}:{kindSegment}:{peerId}";
}
```

- [ ] **Step 6: Record semantic diagnostics into the cache**

In `UpdateRecentSemanticDiagnostics`, after assigning `recentSemanticDiagnostics`, add:

```csharp
if (session.Message is not null)
{
    recentDiagnosticsCache.Record(
        QChatRecentDiagnosticKind.SemanticState,
        BuildRecentDiagnosticsSessionKey(session.Message),
        "qchat_semantic_window",
        diagnostics,
        now);
}
```

- [ ] **Step 7: Record DataAgent evidence diagnostics into the cache**

In `RecordRecentDataAgentEvidenceDiagnostics`, after calling `functionService.RecordRecentDataAgentEvidenceDiagnostics(normalized);`, add:

```csharp
QChatReplySession? replySession = currentReplySession.Value;
string sessionKey = replySession is null
    ? BuildOwnerPrivateRecentDiagnosticsSessionKey()
    : BuildRecentDiagnosticsSessionKey(replySession);
recentDiagnosticsCache.Record(
    QChatRecentDiagnosticKind.DataAgentEvidence,
    sessionKey,
    "dataagent_analysis",
    normalized,
    DateTimeOffset.UtcNow);
```

Add this helper near the session key helpers:

```csharp
string BuildOwnerPrivateRecentDiagnosticsSessionKey()
{
    long botAccountId = Math.Max(0, Configuration?.BotId ?? 0);
    long ownerId = Math.Max(0, Configuration?.OwnerId ?? 0);
    string agentId = QChatAgentIdentityRegistry.CreateDefault().ResolveByBotId(botAccountId)?.AgentId ?? $"qq-{botAccountId}";
    return $"qq:{agentId}:{botAccountId}:private:{ownerId}";
}
```

- [ ] **Step 8: Record Tool Broker diagnostics into the cache**

In `DispatchToModelAsync`, after `recentToolRouteTrace = FormatToolRouteTrace(functionService.RecentToolRouteDecision);`, add:

```csharp
recentDiagnosticsCache.Record(
    QChatRecentDiagnosticKind.ToolRoute,
    BuildRecentDiagnosticsSessionKey(message),
    "tool_broker",
    string.Join(Environment.NewLine,
        "Tool Broker diagnostics",
        $"recent={recentToolRouteTrace}"),
    DateTimeOffset.UtcNow);
```

- [ ] **Step 9: Run runtime cache tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatServiceAdapterTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatRecentDiagnosticsCacheTests" -v:minimal
```

Expected: PASS for the focused QChat runtime diagnostics tests.

- [ ] **Step 10: Commit Task 4**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "Record QChat diagnostics in session cache"
```

---

### Task 5: Readiness And Engineering Gates

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`
- Modify: `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`
- Modify: `tools/check-dataagent-readiness.ps1`
- Modify: `tools/check-qchat-engineering-map.ps1`
- Modify: `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`

- [ ] **Step 1: Add failing DataAgent readiness expectations**

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, update the core readiness count by one and add this assertion near the existing `DataAgentEvidenceDiagnosticsPresent` assertion:

```csharp
Assert.That(checks.Select(check => check.Name), Does.Contain("DataAgentEvidenceRecentDiagnosticsBridgePresent"));
```

Update the summary expectation:

```csharp
"  Summary: 74 required passed, 0 required missing"
```

Update the script contract expectation:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 74"));
```

Add this script contract test:

```csharp
[Test]
public void ReadinessScriptProtectsV27RecentDiagnosticsBridgeContract()
{
    string repoRoot = FindRepoRoot(TestContext.CurrentContext.TestDirectory);
    string scriptPath = Path.Combine(repoRoot, "tools", "check-dataagent-readiness.ps1");
    string script = File.ReadAllText(scriptPath);

    string declaration = FindNewCheckDeclaration(script, "DataAgentEvidenceRecentDiagnosticsBridgePresent");

    Assert.Multiple(() =>
    {
        Assert.That(declaration, Does.Contain("DataAgentAnalysisToolHandler.cs"));
        Assert.That(declaration, Does.Contain("evidenceDiagnosticsPublisher"));
        Assert.That(declaration, Does.Contain("DataAgentEvidenceDiagnosticsFormatter.Format"));
        Assert.That(declaration, Does.Contain("DataAgentModuleService.cs"));
        Assert.That(declaration, Does.Contain("functionService.RecordRecentDataAgentEvidenceDiagnostics"));
    });
}
```

- [ ] **Step 2: Add failing QChat engineering map expectations**

In `Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs`, add:

```csharp
"QChat recent diagnostics cache",
"QChat recent diagnostics command",
"QChat diagnostics cache redaction"
```

In `Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs`, update the QChat engineering map summary expectation:

```csharp
"Summary: 50 required passed, 0 required missing, 0 optional present, 0 optional missing"
```

Update the QChat script count expectation:

```csharp
Assert.That(script, Does.Contain("$expectedRequired = 50"));
```

- [ ] **Step 3: Run failing readiness tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: FAIL until runtime readiness and scripts are updated.

- [ ] **Step 4: Add DataAgent runtime readiness check**

In `sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs`, after `DataAgentEvidenceDiagnosticsPresent`, add:

```csharp
bool recentDiagnosticsBridgeReady =
    typeof(DataAgentAnalysisToolHandler).GetConstructors().Any(ctor =>
        ctor.GetParameters().Any(parameter => parameter.Name == "evidenceDiagnosticsPublisher")) &&
    typeof(DataAgentModuleService).Assembly.GetType("Alife.Function.DataAgent.DataAgentEvidenceDiagnosticsFormatter") is not null;
checks.Add(recentDiagnosticsBridgeReady
    ? Pass("DataAgentEvidenceRecentDiagnosticsBridgePresent", "safe_bridge=true;cache_ready=true")
    : Fail("DataAgentEvidenceRecentDiagnosticsBridgePresent", "safe_bridge=false;cache_ready=false"));
```

This check proves DataAgent still exposes only the safe diagnostics bridge and does not take a direct dependency on QChat cache types.

- [ ] **Step 5: Update DataAgent readiness script**

In `tools/check-dataagent-readiness.ps1`, add this check after `DataAgentEvidenceDiagnosticsPresent`:

```powershell
New-Check -Group "Analysis" -Name "DataAgentEvidenceRecentDiagnosticsBridgePresent" -Passed ((Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentAnalysisToolHandler.cs" @("evidenceDiagnosticsPublisher", "DataAgentEvidenceDiagnosticsFormatter.Format")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.DataAgent/DataAgentModuleService.cs" @("functionService.RecordRecentDataAgentEvidenceDiagnostics")) -and (Test-FileMarker "sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs" @("QChatRecentDiagnosticsCache", "DataAgentEvidence", "hidden_context_redacted"))) -Detail "DataAgent evidence diagnostics bridge feeds QChat recent cache through safe strings"
```

Change:

```powershell
$expectedRequired = 73
```

to:

```powershell
$expectedRequired = 74
```

- [ ] **Step 6: Update QChat engineering map script**

In `tools/check-qchat-engineering-map.ps1`, add these required checks near the existing diagnostics checks:

```powershell
Add-Check -Group "Harness" -Name "QChat recent diagnostics cache" -Path "sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs" -Patterns @("QChatRecentDiagnosticsCache", "maxEntriesPerSession", "GetLatest", "GetRecent", "PruneExpiredLocked")
Add-Check -Group "Harness" -Name "QChat recent diagnostics command" -Path "sources/Alife.Function/Alife.Function.QChat/QChatDiagnosticsService.cs" -Patterns @("diag recent", "BuildRecentDiagnosticsText", "QChatRecentDiagnosticsFormatter.FormatSummary")
Add-Check -Group "Harness" -Name "QChat diagnostics cache redaction" -Path "sources/Alife.Function/Alife.Function.QChat/QChatRecentDiagnosticsCache.cs" -Patterns @("hidden_context_redacted", "[tool_route_context]", "[data_agent_evidence_pack]", "Allowed XML tools")
```

Change:

```powershell
$expectedRequired = 47
```

to:

```powershell
$expectedRequired = 50
```

- [ ] **Step 7: Run readiness tests and scripts**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent Summary: 74 required passed, 0 required missing
QChat Summary: 50 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 8: Commit Task 5**

Run:

```powershell
git add sources/Alife.Function/Alife.Function.DataAgent/DataAgentReadiness.cs Tests/Alife.Test.DataAgent/DataAgentReadinessTests.cs tools/check-dataagent-readiness.ps1 tools/check-qchat-engineering-map.ps1 Tests/Alife.Test.QChat/QChatEngineeringMapRequiredV2Tests.cs
git commit -m "Require recent diagnostics readiness gates"
```

---

### Task 6: Focused Regression Sweep

**Files:**
- Verify all V2.7 changed files.

- [ ] **Step 1: Run focused QChat diagnostics tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatRecentDiagnosticsCacheTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatServiceAdapterTests|FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
```

Expected: PASS.

- [ ] **Step 2: Run focused DataAgent readiness tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
```

Expected: PASS.

- [ ] **Step 3: Run readiness scripts**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
```

Expected:

```text
DataAgent Summary: 74 required passed, 0 required missing
QChat Summary: 50 required passed, 0 required missing, 0 optional present, 0 optional missing
```

- [ ] **Step 4: Run DataAgent and QChat project tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal
```

Expected: PASS. Existing live/environment-gated tests may remain skipped.

- [ ] **Step 5: Run diff hygiene**

Run:

```powershell
git diff --check
git status --short --branch
```

Expected: `git diff --check` exits 0 and branch status shows only V2.7 recent diagnostics cache changes.

---

### Task 7: Final Verification, Review, Merge, And Upload

**Files:**
- Verify repository-wide behavior after V2.7.

- [ ] **Step 1: Run full solution tests**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal -m:1
```

Expected: exit code 0. Existing live/environment-gated tests may remain skipped.

- [ ] **Step 2: Run final diff and branch checks**

Run:

```powershell
git diff --check
git status --short --branch
git log --oneline --decorate -8
```

Expected: diff check exits 0 and the feature branch is clean.

- [ ] **Step 3: Request code review before merge**

Use `superpowers:requesting-code-review` with:

```text
DESCRIPTION: DataAgent/QChat V2.7 session-scoped recent diagnostics cache.
PLAN_OR_REQUIREMENTS: docs/superpowers/plans/2026-07-02-dataagent-v2.7-recent-diagnostics-cache.md
BASE_SHA: master before creating dataagent-v2.7-recent-diagnostics-cache
HEAD_SHA: current feature branch HEAD
```

Required review focus:

- Diagnostics commands do not execute SQL.
- Diagnostics commands do not call tools or `XmlFunctionCaller`.
- Diagnostics commands do not call the model.
- Diagnostics commands do not mutate QChat or DataAgent state.
- Cache entries are session-scoped and capacity-limited.
- Hidden context, raw Evidence Pack tags, SQL, connection strings, and API-key-like text are redacted.
- `/dataagent diag evidence` remains owner-only.
- QChat still does not reference DataAgent orchestration types.

- [ ] **Step 4: Fix Critical or Important review findings**

If review reports Critical or Important findings, use `superpowers:receiving-code-review` and fix them with test-first changes before proceeding.

Expected: no unresolved Critical or Important findings before merge.

- [ ] **Step 5: Merge to master**

From `D:\Alife`, run:

```powershell
git status --short --branch
git merge dataagent-v2.7-recent-diagnostics-cache
```

Expected: merge succeeds, preferably fast-forward.

- [ ] **Step 6: Run post-merge verification on master**

Run from `D:\Alife`:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatRecentDiagnosticsCacheTests|FullyQualifiedName~QChatDiagnosticsServiceTests|FullyQualifiedName~QChatOwnerCommandServiceTests|FullyQualifiedName~QChatServiceAdapterTests|FullyQualifiedName~QChatEngineeringMapRequiredV2Tests" -v:minimal
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.DataAgent\Alife.Test.DataAgent.csproj --no-restore --filter "FullyQualifiedName~DataAgentReadinessTests" -v:minimal
powershell -ExecutionPolicy Bypass -File tools\check-dataagent-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools\check-qchat-engineering-map.ps1
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore -v:minimal -m:1
git diff --check
```

Expected:

```text
DataAgent readiness: 74 required passed, 0 required missing
QChat engineering map: 50 required passed, 0 required missing, 0 optional present, 0 optional missing
Full solution: exit code 0
```

- [ ] **Step 7: Push to GitHub**

Run:

```powershell
git push alife-byastralfox master
git ls-remote alife-byastralfox refs/heads/master
```

Expected: remote `refs/heads/master` points to the merged V2.7 commit.

- [ ] **Step 8: Clean up feature worktree**

After successful push and remote verification, run:

```powershell
git worktree remove "D:\Alife\.worktrees\dataagent-v2.7-recent-diagnostics-cache"
git worktree prune
git branch -d dataagent-v2.7-recent-diagnostics-cache
```

If Windows reports `Filename too long`, first verify the exact target path:

```powershell
Resolve-Path -LiteralPath "D:\Alife\.worktrees\dataagent-v2.7-recent-diagnostics-cache"
git worktree list
```

Then remove only that verified residual directory with the long-path prefix:

```powershell
Remove-Item -LiteralPath "\\?\D:\Alife\.worktrees\dataagent-v2.7-recent-diagnostics-cache" -Recurse -Force
git worktree prune
git branch -d dataagent-v2.7-recent-diagnostics-cache
```

---

## Plan Self-Review

- Spec coverage: the plan covers session-scoped cache entries, kind-based reads, TTL, capacity, redaction, `/qchat diag recent`, cache-first semantic/evidence/Tool Broker commands, QChat runtime recording, DataAgent bridge readiness, QChat engineering gates, focused verification, full verification, merge, upload, and cleanup.
- Open-item scan: every implementation task has exact file paths, concrete code snippets, commands, and expected outcomes.
- Type consistency: `QChatRecentDiagnosticKind`, `QChatRecentDiagnosticEntry`, `QChatRecentDiagnosticsCache`, `QChatRecentDiagnosticsFormatter`, `RecentDiagnosticsCache`, `SessionKey`, and `DiagnosticsNow` are defined before later tasks reference them.
- Boundary check: DataAgent still emits safe evidence diagnostics strings; QChat owns the cache; diagnostics commands remain read-only; Tool Broker remains the only permission authority.
- Testability check: all new behavior can be verified without live QQ, live model calls, live SQL, PostgreSQL, or LangGraph.
