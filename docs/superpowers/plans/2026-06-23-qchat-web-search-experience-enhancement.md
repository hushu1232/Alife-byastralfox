# QChat Web Search Experience Enhancement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve QChat public web search so QQ users can trigger it semantically with fewer false positives, receive concise source-backed replies, and stay within existing safety and token controls.

**Architecture:** Keep parsing, permissioning, research, and formatting separate. `QChatPublicInternetCommandPolicy` owns deterministic trigger parsing. `AgentWebResearchService` owns search/read/cache/cooldown/concurrency. `QChatWebResearchFormatter` owns QQ-sized output shaping. `QChatService` wires the chain and records diagnostics without bypassing existing permission checks.

**Tech Stack:** C#/.NET 9, NUnit, existing QChat service adapter fakes, existing `AgentWebResearchService` and public search provider abstractions.

---

## File Structure

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatPublicInternetCommandPolicy.cs`
  - Strengthen semantic trigger extraction.
  - Keep `/qchat` excluded.
  - Keep group semantic search gated by bot mention.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatWebResearchFormatter.cs`
  - Add role-aware QQ formatting.
  - Bound answer length, evidence count, and source count.

- Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Pass formatting context to formatter.
  - Add diagnostics around web research command evaluation and result handling.
  - Preserve model bypass only for allowed web research commands.

- Modify `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`
  - Keep current search/read/cache/cooldown behavior.
  - Adjust answer composition only if formatter needs cleaner evidence text.

- Create or modify `Tests/Alife.Test.QChat/QChatPublicInternetCommandPolicyTests.cs`
  - Cover semantic trigger and false-positive cases.

- Create or modify `Tests/Alife.Test.QChat/QChatWebResearchFormatterTests.cs`
  - Cover short group-member output, richer owner output, failure output, and length bounds.

- Modify `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
  - Cover group mention semantic search, no-mention no-search, ordinary chat no-search, cooldown, and no model dispatch for accepted search.

- Modify `Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs`
  - Cover owner freshness-aware query and group-member no-read behavior if not already covered.

---

### Task 1: Strengthen Semantic Search Trigger Parsing

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatPublicInternetCommandPolicy.cs`
- Test: `Tests/Alife.Test.QChat/QChatPublicInternetCommandPolicyTests.cs`

- [ ] **Step 1: Write failing parser tests**

Add these tests to `Tests/Alife.Test.QChat/QChatPublicInternetCommandPolicyTests.cs`:

```csharp
[Test]
public void ParseMessage_GroupMentionSearchPhrase_ReturnsSearchCommand()
{
    QChatPublicInternetCommand command = QChatPublicInternetCommandPolicy.ParseMessage(
        OneBotMessageType.Group,
        999,
        "[CQ:at,qq=999] 搜一下 Agent-Reach 项目",
        "搜一下 Agent-Reach 项目");

    Assert.That(command.Kind, Is.EqualTo(QChatPublicInternetCommandKind.Search));
    Assert.That(command.Query, Is.EqualTo("Agent-Reach 项目"));
}

[Test]
public void ParseMessage_GroupWithoutMention_DoesNotReturnSearchCommand()
{
    QChatPublicInternetCommand command = QChatPublicInternetCommandPolicy.ParseMessage(
        OneBotMessageType.Group,
        999,
        "搜一下 Agent-Reach 项目",
        "搜一下 Agent-Reach 项目");

    Assert.That(command.Kind, Is.EqualTo(QChatPublicInternetCommandKind.None));
}

[Test]
public void ParseMessage_OrdinaryGroupChat_DoesNotReturnSearchCommand()
{
    QChatPublicInternetCommand command = QChatPublicInternetCommandPolicy.ParseMessage(
        OneBotMessageType.Group,
        999,
        "[CQ:at,qq=999] 你觉得这个怎么样",
        "你觉得这个怎么样");

    Assert.That(command.Kind, Is.EqualTo(QChatPublicInternetCommandKind.None));
}

[Test]
public void ParseMessage_PrivateLatestPhrase_ReturnsCleanSearchQuery()
{
    QChatPublicInternetCommand command = QChatPublicInternetCommandPolicy.ParseMessage(
        OneBotMessageType.Private,
        999,
        "查最新 dotnet 10 发布时间",
        "查最新 dotnet 10 发布时间");

    Assert.That(command.Kind, Is.EqualTo(QChatPublicInternetCommandKind.Search));
    Assert.That(command.Query, Is.EqualTo("dotnet 10 发布时间"));
}
```

- [ ] **Step 2: Run parser tests and observe failure**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter QChatPublicInternetCommandPolicyTests
```

Expected before implementation:

```text
At least one new semantic trigger test fails because current extraction is too narrow or keeps trigger words in the query.
```

- [ ] **Step 3: Implement deterministic trigger extraction**

In `QChatPublicInternetCommandPolicy.ParseMessage(...)`, keep the existing `/search` and `/rag` handling. Replace or extend `ExtractSearchQuery(...)` with this shape:

```csharp
static string ExtractSearchQuery(string plainText)
{
    string text = NormalizePlainSearchText(plainText);
    if (text.Length == 0)
        return "";

    string[] prefixes =
    [
        "搜一下",
        "搜索一下",
        "查一下",
        "帮我查",
        "帮我找",
        "联网查",
        "查最新",
        "找资料",
        "有没有公开信息",
        "search",
        "look up"
    ];

    foreach (string prefix in prefixes)
    {
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
            continue;

        string query = text[prefix.Length..].Trim(' ', '：', ':', '，', ',');
        return NormalizeQueryTail(query);
    }

    return "";
}

static string NormalizePlainSearchText(string text)
{
    text = OneBotSegment.GetPlainText(text ?? "");
    text = Regex.Replace(text, @"^\s*@?\S+\s+", "");
    return Regex.Replace(text.Trim(), @"\s+", " ");
}

static string NormalizeQueryTail(string query)
{
    query = Regex.Replace(query.Trim(), @"\s+", " ");
    return query.Length > 0 && query.Length <= 160 ? query : "";
}
```

If the file already has helper names with overlapping responsibility, reuse the existing names and keep the behavior above.

- [ ] **Step 4: Run parser tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter QChatPublicInternetCommandPolicyTests
```

Expected:

```text
Passed! - Failed: 0
```

- [ ] **Step 5: Commit task changes**

If executing in an isolated clean worktree:

```powershell
git add sources\Alife.Function\Alife.Function.QChat\QChatPublicInternetCommandPolicy.cs Tests\Alife.Test.QChat\QChatPublicInternetCommandPolicyTests.cs
git commit -m "feat: refine QChat web search triggers"
```

If executing in the dirty `D:\Alife` main worktree, do not commit mixed unrelated staged changes. Stage only these files and defer commit/upload to the final upload workflow.

---

### Task 2: Add Role-Aware QQ Web Research Formatter

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatWebResearchFormatter.cs`
- Test: `Tests/Alife.Test.QChat/QChatWebResearchFormatterTests.cs`

- [ ] **Step 1: Write failing formatter tests**

Create `Tests/Alife.Test.QChat/QChatWebResearchFormatterTests.cs`:

```csharp
using Alife.Function.Agent;
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

public sealed class QChatWebResearchFormatterTests
{
    [Test]
    public void Format_GroupMemberSuccess_ReturnsShortConclusionAndTwoSources()
    {
        AgentWebResearchResult result = new(
            true,
            "ok",
            "agent browser",
            "结论：长答案不应该原样刷屏。",
            [
                new AgentWebResearchEvidence("Docs", "https://docs.example.com", "official docs summary", "docs"),
                new AgentWebResearchEvidence("GitHub", "https://github.com/example/repo", "repo summary", "github"),
                new AgentWebResearchEvidence("Blog", "https://blog.example.com", "blog summary", "web")
            ]);

        string text = QChatWebResearchFormatter.Format(result, new QChatWebResearchFormatContext(
            QChatSenderRole.GroupMember,
            OneBotMessageType.Group));

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("结论："));
            Assert.That(text, Does.Contain("Docs"));
            Assert.That(text, Does.Contain("GitHub"));
            Assert.That(text, Does.Not.Contain("Blog"));
            Assert.That(text.Length, Is.LessThanOrEqualTo(420));
        });
    }

    [Test]
    public void Format_OwnerSuccess_AllowsThreeSources()
    {
        AgentWebResearchResult result = new(
            true,
            "ok",
            "agent browser",
            "结论：owner answer",
            [
                new AgentWebResearchEvidence("Docs", "https://docs.example.com", "official docs summary", "docs"),
                new AgentWebResearchEvidence("GitHub", "https://github.com/example/repo", "repo summary", "github"),
                new AgentWebResearchEvidence("Release", "https://example.com/release", "release summary", "web")
            ]);

        string text = QChatWebResearchFormatter.Format(result, new QChatWebResearchFormatContext(
            QChatSenderRole.Owner,
            OneBotMessageType.Private));

        Assert.That(text, Does.Contain("Release"));
        Assert.That(text.Length, Is.LessThanOrEqualTo(760));
    }

    [Test]
    public void Format_Failure_ReturnsShortReadableMessage()
    {
        AgentWebResearchResult result = new(false, "web_research_cooldown", "x", "web_research_rate_limited: cooldown", []);

        string text = QChatWebResearchFormatter.Format(result, new QChatWebResearchFormatContext(
            QChatSenderRole.GroupMember,
            OneBotMessageType.Group));

        Assert.That(text, Is.EqualTo("搜太快了，等一下。"));
    }
}
```

- [ ] **Step 2: Run formatter tests and observe failure**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter QChatWebResearchFormatterTests
```

Expected before implementation:

```text
Compilation fails because QChatWebResearchFormatContext or the new overload does not exist.
```

- [ ] **Step 3: Implement formatter context and bounded formatting**

Replace `QChatWebResearchFormatter.cs` with this structure, preserving namespace and using directives:

```csharp
using Alife.Function.Agent;

namespace Alife.Function.QChat;

public sealed record QChatWebResearchFormatContext(
    QChatSenderRole SenderRole,
    OneBotMessageType MessageType);

public static class QChatWebResearchFormatter
{
    public static string Format(AgentWebResearchResult result) =>
        Format(result, new QChatWebResearchFormatContext(QChatSenderRole.Unknown, OneBotMessageType.Private));

    public static string Format(AgentWebResearchResult result, QChatWebResearchFormatContext context)
    {
        if (result.Success == false)
            return FormatFailure(result);

        int sourceLimit = context.SenderRole == QChatSenderRole.Owner ? 3 : 2;
        int maxLength = context.SenderRole == QChatSenderRole.Owner ? 760 : 420;
        string conclusion = ExtractConclusion(result.Answer);
        IEnumerable<AgentWebResearchEvidence> evidence = result.Evidence.Take(sourceLimit);

        List<string> lines = [conclusion];
        foreach (AgentWebResearchEvidence item in evidence)
            lines.Add($"- {Compact(item.Title, 32)}：{Compact(item.Summary, 90)}");

        string sources = "来源：" + string.Join(" / ", result.Evidence
            .Take(sourceLimit)
            .Select(item => $"{Compact(item.Title, 24)} {item.Url}"));
        if (sources.Length > 3)
            lines.Add(sources);

        return Limit(string.Join(Environment.NewLine, lines), maxLength);
    }

    static string FormatFailure(AgentWebResearchResult result)
    {
        return result.Reason switch
        {
            "web_research_cooldown" => "搜太快了，等一下。",
            "web_research_busy" => "现在搜索队列有点满，稍后再试。",
            "empty_query" => "你要我搜什么？",
            "no_results" => "没查到可靠来源。",
            "public_search_not_configured" => "搜索现在不可用。",
            _ => string.IsNullOrWhiteSpace(result.Answer) ? "搜索失败，先不乱说。" : result.Answer
        };
    }

    static string ExtractConclusion(string answer)
    {
        string firstLine = (answer ?? "").Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (firstLine.StartsWith("结论：", StringComparison.Ordinal))
            return Compact(firstLine, 140);
        return "结论：" + Compact(firstLine.Length == 0 ? "查到了公开资料。" : firstLine, 120);
    }

    static string Compact(string value, int maxChars)
    {
        value = string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "...";
    }

    static string Limit(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars].TrimEnd() + "...";
}
```

- [ ] **Step 4: Run formatter tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter QChatWebResearchFormatterTests
```

Expected:

```text
Passed! - Failed: 0
```

- [ ] **Step 5: Commit task changes**

If executing in an isolated clean worktree:

```powershell
git add sources\Alife.Function\Alife.Function.QChat\QChatWebResearchFormatter.cs Tests\Alife.Test.QChat\QChatWebResearchFormatterTests.cs
git commit -m "feat: format QChat web research replies"
```

If executing in the dirty `D:\Alife` main worktree, stage only these files and defer commit/upload.

---

### Task 3: Wire Formatter Context And Diagnostics In QChat Service

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Test: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Write failing service tests**

Add tests near existing public internet/web research tests in `QChatServiceAdapterTests.cs`:

```csharp
[Test]
public async Task PublicSearchGroupMentionSemanticTriggerSearchesAndDoesNotDispatchModel()
{
    FakeOneBotRuntime runtime = new();
    FakePublicSearchProvider provider = new(
        new AgentPublicSearchResult("Agent Reach", "https://github.com/Panniantong/Agent-Reach", "agent reach snippet"));
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        AllowedGroupIds = "3003",
        AllowGroupMemberChat = true,
        AllowGroupMemberMentions = true,
        EnablePublicInternetSearch = true,
        EnableBalancedTextStreaming = false
    }, publicSearchProvider: provider);
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        Interlocked.Increment(ref dispatchCount);
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 2002,
        GroupId = 3003,
        RawMessage = "[CQ:at,qq=999] 搜一下 Agent-Reach 项目"
    });

    await WaitUntilAsync(() => runtime.GroupMessages.Count == 1, TimeSpan.FromSeconds(2));
    Assert.Multiple(() =>
    {
        Assert.That(provider.Calls, Is.EqualTo(1));
        Assert.That(provider.LastQuery, Is.EqualTo("Agent-Reach 项目"));
        Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("结论："));
        Assert.That(dispatchCount, Is.Zero);
    });
}

[Test]
public async Task PublicSearchGroupNoMentionDoesNotSearch()
{
    FakeOneBotRuntime runtime = new();
    FakePublicSearchProvider provider = new(
        new AgentPublicSearchResult("Agent Reach", "https://github.com/Panniantong/Agent-Reach", "agent reach snippet"));
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        AllowedGroupIds = "3003",
        AllowGroupMemberChat = true,
        AllowProactiveGroupChat = true,
        ProactiveChatProbability = 0,
        EnablePublicInternetSearch = true,
        EnableBalancedTextStreaming = false
    }, publicSearchProvider: provider);
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        Interlocked.Increment(ref dispatchCount);
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 2002,
        GroupId = 3003,
        RawMessage = "搜一下 Agent-Reach 项目"
    });

    await Task.Delay(200);
    Assert.That(provider.Calls, Is.Zero);
}
```

- [ ] **Step 2: Run service tests and observe failure**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "PublicSearchGroupMentionSemanticTriggerSearchesAndDoesNotDispatchModel|PublicSearchGroupNoMentionDoesNotSearch"
```

Expected before implementation:

```text
At least one test fails because semantic extraction or formatter context is not wired.
```

- [ ] **Step 3: Pass formatter context in QChatService**

Find the existing web research send path in `QChatService.cs` and replace:

```csharp
QChatWebResearchFormatter.Format(research)
```

with:

```csharp
QChatWebResearchFormatter.Format(
    research,
    new QChatWebResearchFormatContext(senderRole, messageEvent.MessageType))
```

- [ ] **Step 4: Add diagnostics around command evaluation**

Near the web research command handling block, add diagnostics shaped like:

```csharp
WriteQChatDiagnostic("qchat-web-research-command", "QChat public web research command was evaluated.", new {
    messageEvent.MessageType,
    messageEvent.UserId,
    messageEvent.GroupId,
    senderRole,
    command.Kind,
    command.Query,
    decision.Allowed,
    decision.Reason
});
```

After research returns, add:

```csharp
WriteQChatDiagnostic("qchat-web-research-result", "QChat public web research command completed.", new {
    messageEvent.MessageType,
    messageEvent.UserId,
    messageEvent.GroupId,
    senderRole,
    research.Success,
    research.Reason,
    EvidenceCount = research.Evidence.Count,
    OwnerPageReadEnabled = senderRole == QChatSenderRole.Owner && config.EnableInternetAccess
});
```

Do not log page bodies, tokens, API keys, or raw fetched content.

- [ ] **Step 5: Run service tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "PublicSearchGroupMentionSemanticTriggerSearchesAndDoesNotDispatchModel|PublicSearchGroupNoMentionDoesNotSearch|WebResearchGroupMentionCooldownDoesNotSearchOrDispatchModel|NonOwnerCannotTriggerInternetLookupOrModel"
```

Expected:

```text
Passed! - Failed: 0
```

- [ ] **Step 6: Commit task changes**

If executing in an isolated clean worktree:

```powershell
git add sources\Alife.Function\Alife.Function.QChat\QChatService.cs Tests\Alife.Test.QChat\QChatServiceAdapterTests.cs
git commit -m "feat: wire QChat web research formatting"
```

If executing in the dirty `D:\Alife` main worktree, stage only these files and defer commit/upload.

---

### Task 4: Verify Research Service Token Controls And Owner/Member Difference

**Files:**
- Modify: `Tests/Alife.Test.Framework/AgentWebResearchServiceTests.cs`
- Modify only if needed: `sources/Alife.Function/Alife.Function.MessageFilter/AgentWebResearchService.cs`

- [ ] **Step 1: Add tests for group member no-read and owner read**

Add or keep tests with these assertions:

```csharp
[Test]
public async Task ResearchAsync_GroupMemberUsesSearchEvidenceWithoutReadingPages()
{
    FakePublicSearchService search = new([
        new AgentPublicSearchResult("Public Result", "https://example.com/public", "public search snippet")
    ]);
    FakeInternetService internet = new(new AgentInternetFetchResult(true, "ok", "should not be read"));
    AgentWebResearchService service = new(search, new AgentWebAccessService(internetService: internet));

    AgentWebResearchResult result = await service.ResearchAsync(new AgentWebResearchRequest(
        "public topic",
        AgentWebAccessActorRole.GroupMember,
        new AgentWebAccessConfig
        {
            EnablePublicSearch = true,
            AllowGroupMemberPublicSearch = true,
            EnablePublicFetch = true
        }));

    Assert.Multiple(() =>
    {
        Assert.That(result.Success, Is.True);
        Assert.That(search.Calls, Is.EqualTo(1));
        Assert.That(internet.Calls, Is.Zero);
        Assert.That(result.Evidence.Single().Summary, Does.Contain("public search snippet"));
    });
}
```

If this exact behavior is already covered, do not duplicate the test. Keep the existing test and add only missing assertions.

- [ ] **Step 2: Add test for cached result avoiding repeated search**

Ensure this behavior is covered:

```csharp
[Test]
public async Task ResearchAsync_ReusesCachedResultBeforeSearchingAgain()
{
    AgentWebResearchControlState control = new();
    FakePublicSearchService search = new([
        new AgentPublicSearchResult("Cached", "https://example.com/cached", "cached snippet")
    ]);
    AgentWebResearchService service = new(search, new AgentWebAccessService(), controlState: control);
    AgentWebResearchRequest request = new(
        "cached topic",
        AgentWebAccessActorRole.GroupMember,
        new AgentWebAccessConfig
        {
            EnablePublicSearch = true,
            AllowGroupMemberPublicSearch = true,
            WebResearchCacheSeconds = 120
        },
        ActorUserId: 2002,
        GroupId: 3003);

    AgentWebResearchResult first = await service.ResearchAsync(request);
    AgentWebResearchResult second = await service.ResearchAsync(request);

    Assert.Multiple(() =>
    {
        Assert.That(first.Success, Is.True);
        Assert.That(second.Success, Is.True);
        Assert.That(search.Calls, Is.EqualTo(1));
        Assert.That(control.GetMetricsSnapshot().CacheHits, Is.EqualTo(1));
    });
}
```

If the test already exists with the same assertions, do not duplicate it.

- [ ] **Step 3: Run framework web research tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter AgentWebResearchServiceTests
```

Expected:

```text
Passed! - Failed: 0
```

- [ ] **Step 4: Implement only missing behavior**

If group-member no-read or cache behavior fails, adjust `AgentWebResearchService.ResearchCoreAsync(...)` so group member requests use `BuildSearchEvidence(result)` and never call `TryReadOwnerEvidenceAsync(...)`:

```csharp
AgentWebResearchEvidence? item = request.ActorRole == AgentWebAccessActorRole.Owner
    ? await TryReadOwnerEvidenceAsync(result, request.Config, cancellationToken)
    : BuildSearchEvidence(result);
```

If cache behavior fails, keep cache lookup before cooldown and search:

```csharp
if (controlState.TryGetCachedResult(request, query, maxSources, out AgentWebResearchResult cached))
    return cached;
```

- [ ] **Step 5: Commit task changes**

If executing in an isolated clean worktree:

```powershell
git add sources\Alife.Function\Alife.Function.MessageFilter\AgentWebResearchService.cs Tests\Alife.Test.Framework\AgentWebResearchServiceTests.cs
git commit -m "test: cover web research resource controls"
```

If executing in the dirty `D:\Alife` main worktree, stage only these files and defer commit/upload.

---

### Task 5: Final Verification And Upload

**Files:**
- Verify all modified QChat/web research files.
- Upload through `D:\FOXD` only after tests pass.

- [ ] **Step 1: Run focused QChat tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatPublicInternetCommandPolicyTests|QChatWebResearchFormatterTests|PublicSearchGroupMentionSemanticTriggerSearchesAndDoesNotDispatchModel|PublicSearchGroupNoMentionDoesNotSearch|WebResearchGroupMentionCooldownDoesNotSearchOrDispatchModel|NonOwnerCannotTriggerInternetLookupOrModel"
```

Expected:

```text
Passed! - Failed: 0
```

- [ ] **Step 2: Run focused framework tests**

Run:

```powershell
dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter "AgentWebResearchServiceTests|MojibakeRegressionTests"
```

Expected:

```text
Passed! - Failed: 0
```

- [ ] **Step 3: Inspect relevant diff**

Run:

```powershell
git diff -- sources\Alife.Function\Alife.Function.QChat\QChatPublicInternetCommandPolicy.cs sources\Alife.Function\Alife.Function.QChat\QChatWebResearchFormatter.cs sources\Alife.Function\Alife.Function.QChat\QChatService.cs sources\Alife.Function\Alife.Function.MessageFilter\AgentWebResearchService.cs Tests\Alife.Test.QChat\QChatPublicInternetCommandPolicyTests.cs Tests\Alife.Test.QChat\QChatWebResearchFormatterTests.cs Tests\Alife.Test.QChat\QChatServiceAdapterTests.cs Tests\Alife.Test.Framework\AgentWebResearchServiceTests.cs
```

Expected:

```text
Diff is limited to web search trigger parsing, QQ formatting, service integration diagnostics, and tests.
```

- [ ] **Step 4: Upload through carrier workflow**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-service-via-foxd.ps1
```

Expected:

```text
Push succeeds and remote refs/heads/master updates.
```

- [ ] **Step 5: Report final state**

Include:

- files changed;
- tests run and results;
- GitHub commit hash if uploaded;
- remaining gaps: not a full browser agent, no login/form automation, full solution tests not run unless explicitly run.

---

## Self-Review

- Spec coverage: trigger accuracy, formatter quality, token controls, diagnostics, owner/member permissions, and tests each map to a task.
- 占位项扫描：没有留下空任务或模糊执行项。
- Type consistency: `QChatWebResearchFormatContext`, `QChatWebResearchFormatter.Format(...)`, `QChatPublicInternetCommandPolicy.ParseMessage(...)`, and existing `AgentWebResearchService` types are named consistently.
- Scope check: this plan enhances first-phase web search and does not attempt full browser automation or RAG redesign.

---

## Execution Status 2026-06-23

- Task 1 completed: semantic public search triggers now require bot mention in groups, strip known search prefixes, keep `/qchat` excluded, and preserve explicit `/search` and `/rag` command behavior.
- Task 2 completed: QChat web research formatting is role-aware, with shorter group-member output, richer owner output, bounded source count, and short failure messages.
- Task 3 completed: QChat service passes sender/message context into the formatter, writes public web research command/result diagnostics, and bypasses model dispatch only for accepted web research commands.
- Task 3 review fix completed: `PublicSearchGroupNoMentionDoesNotSearch` no longer relies on fixed delay; diagnostics tests no longer hard-code enum numeric JSON values.
- Task 4 verified: `AgentWebResearchServiceTests` covers owner page read, group-member no-read behavior, cache reuse, cooldown, and token/resource metrics without requiring production changes.
- Verification completed so far:
  - `dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "PublicSearchGroupMentionSemanticTriggerSearchesAndDoesNotDispatchModel|PublicSearchGroupNoMentionDoesNotSearch|WebResearchGroupMentionCooldownDoesNotSearchOrDispatchModel|NonOwnerCannotTriggerInternetLookupOrModel"` -> 4 passed, 0 failed.
  - `dotnet test Tests\Alife.Test.Framework\Alife.Test.Framework.csproj --filter AgentWebResearchServiceTests` -> 20 passed, 0 failed.

---

## Final Status 2026-06-23

- Completed: first-phase QQ web search experience enhancement.
- Completed: semantic `@bot` group search trigger, role-aware QQ result formatting, diagnostics, cooldown/cache/resource-control tests, and GitHub upload.
- Uploaded: GitHub carrier remote `github/master` verified at `8f5b14d9856fb387930378c3cb2a353f463a1885`.
- Not complete: full browser-agent automation is still unfinished.
- Browser-agent unfinished scope:
  - no general page open/scroll/click workflow exposed to QQ;
  - no multi-page browsing plan executor;
  - no login/form workflow;
  - no screenshot-to-strategy feedback loop in daily QQ chat;
  - no browser session state policy for QQ users;
  - no full web automation live smoke path.
- Boundary: this plan closes the QQ public web search/RAG-like entry path, but does not close the larger browser automation Agent phase.
