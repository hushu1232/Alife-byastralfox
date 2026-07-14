# Persona-Aware Owner Engineering Replies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Add a typed owner-engineering-event reply contract whose persona lead never alters facts, verification outcomes, uncertainty, or failures.

**Architecture:** A pure formatter accepts an immutable reply payload that separates stage from caller-owned factual fields. The existing owner-event request and JSONL entry gain an optional EngineeringReply; the dispatcher selects the new formatter only for events carrying that value. Generic events continue to use QChatCommandPersonaFormatter unchanged.

**Tech Stack:** .NET 9, C#, NUnit, QChat JSONL outbox, OneBot runtime adapter.

---

## File Structure

- Create: sources/Alife.Function/Alife.Function.QChat/QChatOwnerEngineeringReply.cs — stage enum, immutable payload, pure formatter.
- Modify: sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventOutbox.cs — optional typed payload persisted alongside generic event text.
- Modify: sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs — conditional typed-event formatter route.
- Create: Tests/Alife.Test.QChat/QChatOwnerEngineeringReplyFormatterTests.cs — fact/persona, blocked, complete, audience, and blank-input tests.
- Modify: Tests/Alife.Test.QChat/QChatOwnerEventDispatcherTests.cs — typed dispatch behavior and generic-route regression.

### Task 1: Build the pure formatter with TDD

**Files:**
- Create: Tests/Alife.Test.QChat/QChatOwnerEngineeringReplyFormatterTests.cs
- Create: sources/Alife.Function/Alife.Function.QChat/QChatOwnerEngineeringReply.cs

- [ ] **Step 1: Write the failing formatter tests**

Create Tests/Alife.Test.QChat/QChatOwnerEngineeringReplyFormatterTests.cs:

~~~csharp
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public sealed class QChatOwnerEngineeringReplyFormatterTests
{
    [Test]
    public void FormatForXiayuOwnerRetainsFactsAndVerificationVerbatim()
    {
        QChatOwnerEngineeringReply reply = new(
            QChatOwnerEngineeringReplyStage.Hypothesis,
            "candidate=QZoneService.Report",
            "tests=not-run");

        string formatted = QChatOwnerEngineeringReplyFormatter.Format("xiayu", QChatSenderRole.Owner, reply);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.StartWith("术术，"));
            Assert.That(formatted, Does.Contain("candidate=QZoneService.Report"));
            Assert.That(formatted, Does.Contain("tests=not-run"));
        });
    }

    [Test]
    public void FormatBlockedReplyRetainsFailureWithoutCompleteLead()
    {
        QChatOwnerEngineeringReply reply = new(
            QChatOwnerEngineeringReplyStage.Blocked,
            "checked=qq-send-exit,file-runner",
            UncertaintyOrFailure: "missing_evidence=correlation-id");

        string formatted = QChatOwnerEngineeringReplyFormatter.Format("xiayu", QChatSenderRole.Owner, reply);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.Contain("missing_evidence=correlation-id"));
            Assert.That(formatted, Does.Not.Contain("已处理完"));
            Assert.That(formatted, Does.Not.Contain("完成了"));
        });
    }

    [Test]
    public void FormatCompleteReplyRetainsExactVerificationAndFailureText()
    {
        QChatOwnerEngineeringReply reply = new(
            QChatOwnerEngineeringReplyStage.Complete,
            "path=qchat-owner-event-dispatcher",
            "tests=5 passed, 0 failed",
            "live_validation=not-run");

        string formatted = QChatOwnerEngineeringReplyFormatter.Format("mixu", QChatSenderRole.Owner, reply);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.StartWith("主人，"));
            Assert.That(formatted, Does.Contain("tests=5 passed, 0 failed"));
            Assert.That(formatted, Does.Contain("live_validation=not-run"));
            Assert.That(formatted, Does.Not.Contain("术术"));
        });
    }

    [Test]
    public void FormatForNonOwnerUsesNeutralLead()
    {
        QChatOwnerEngineeringReply reply = new(QChatOwnerEngineeringReplyStage.Intake, "goal=remove-internal-label");

        string formatted = QChatOwnerEngineeringReplyFormatter.Format("xiayu", QChatSenderRole.PrivateGuest, reply);

        Assert.Multiple(() =>
        {
            Assert.That(formatted, Does.StartWith("工程状态如下。"));
            Assert.That(formatted, Does.Not.Contain("术术"));
            Assert.That(formatted, Does.Not.Contain("主人"));
        });
    }

    [Test]
    public void FormatReturnsEmptyWhenFactsAreBlank()
    {
        QChatOwnerEngineeringReply reply = new(QChatOwnerEngineeringReplyStage.Intake, "   ", "tests=not-run");

        Assert.That(QChatOwnerEngineeringReplyFormatter.Format("xiayu", QChatSenderRole.Owner, reply), Is.Empty);
    }
}
~~~

- [ ] **Step 2: Verify the RED state**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter 'FullyQualifiedName~QChatOwnerEngineeringReplyFormatterTests' -v:minimal
~~~

Expected: compilation fails because the three engineering-reply types are missing.

- [ ] **Step 3: Add the minimal formatter implementation**

Create sources/Alife.Function/Alife.Function.QChat/QChatOwnerEngineeringReply.cs:

~~~csharp
using System;
using System.Linq;

namespace Alife.Function.QChat;

public enum QChatOwnerEngineeringReplyStage
{
    Intake,
    Hypothesis,
    Blocked,
    Complete
}

public sealed record QChatOwnerEngineeringReply(
    QChatOwnerEngineeringReplyStage Stage,
    string Facts,
    string? Verification = null,
    string? UncertaintyOrFailure = null);

public static class QChatOwnerEngineeringReplyFormatter
{
    public static string Format(string? agentId, QChatSenderRole senderRole, QChatOwnerEngineeringReply? reply)
    {
        if (reply is null)
            return "";

        string facts = reply.Facts?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(facts))
            return "";

        string agent = string.IsNullOrWhiteSpace(agentId) ? "" : agentId.Trim().ToLowerInvariant();
        string stageLead = reply.Stage switch
        {
            QChatOwnerEngineeringReplyStage.Intake => "我会先收窄路径。",
            QChatOwnerEngineeringReplyStage.Hypothesis => "目前的判断在这里。",
            QChatOwnerEngineeringReplyStage.Blocked => "这条路径暂时卡住了。",
            QChatOwnerEngineeringReplyStage.Complete => "这条路径已处理完。",
            _ => "工程状态如下。"
        };
        string lead = senderRole == QChatSenderRole.Owner
            ? agent switch
            {
                "xiayu" => $"术术，{stageLead}",
                "mixu" => $"主人，{stageLead}",
                _ => $"工程状态如下。{stageLead}"
            }
            : "工程状态如下。";

        return string.Join(Environment.NewLine, new[]
        {
            lead,
            facts,
            reply.Verification?.Trim() ?? "",
            reply.UncertaintyOrFailure?.Trim() ?? ""
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
~~~

The formatter must not sanitize, authorize, infer, or rewrite payload fields.

- [ ] **Step 4: Verify the GREEN state**

Run the Step 2 command again.

Expected: 5 tests pass; known test-project unused-event warnings may remain.

- [ ] **Step 5: Commit the formatter**

~~~powershell
git add Tests/Alife.Test.QChat/QChatOwnerEngineeringReplyFormatterTests.cs sources/Alife.Function/Alife.Function.QChat/QChatOwnerEngineeringReply.cs
git commit -m "feat: add persona-aware engineering reply formatter"
~~~

### Task 2: Persist and dispatch typed engineering events

**Files:**
- Modify: sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventOutbox.cs:19-45,75-105,225-238
- Modify: sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs:35-46
- Modify: Tests/Alife.Test.QChat/QChatOwnerEventDispatcherTests.cs

- [ ] **Step 1: Write a failing typed-event dispatch test**

Add this test after FlushAsyncSendsPendingEventsAndMarksDelivered in Tests/Alife.Test.QChat/QChatOwnerEventDispatcherTests.cs:

~~~csharp
[Test]
public async Task FlushAsyncFormatsTypedEngineeringEventWithoutChangingGenericEvents()
{
    QChatOwnerEventOutbox outbox = new(CreateTempPath());
    FakeOneBotRuntime runtime = new() { NextMessageId = 10 };
    QChatOwnerEventEntry genericEntry = outbox.Enqueue(CreateRequest("generic"));
    QChatOwnerEventEntry engineeringEntry = outbox.Enqueue(new QChatOwnerEventRequest(
        DedupeKey: "engineering", AgentId: "xiayu", OwnerId: 1001,
        Severity: "info", Category: "engineering", Source: "test", SourceId: "engineering",
        Message: "generic-message-must-not-be-sent",
        EngineeringReply: new QChatOwnerEngineeringReply(
            QChatOwnerEngineeringReplyStage.Blocked,
            "checked=qchat-owner-event-dispatcher",
            "tests=not-run",
            "missing_evidence=correlation-id")));
    QChatOwnerEventDispatcher dispatcher = new(outbox, () => runtime);

    int delivered = await dispatcher.FlushAsync();

    string genericMessage = runtime.PrivateMessages.Single(message =>
        message.Message.Contains("action=test result=success", StringComparison.Ordinal)).Message;
    string engineeringMessage = runtime.PrivateMessages.Single(message =>
        message.Message.Contains("checked=qchat-owner-event-dispatcher", StringComparison.Ordinal)).Message;
    Assert.Multiple(() =>
    {
        Assert.That(delivered, Is.EqualTo(2));
        Assert.That(genericMessage, Does.StartWith("术术，我看过了。"));
        Assert.That(engineeringMessage, Does.StartWith("术术，"));
        Assert.That(engineeringMessage, Does.Contain("tests=not-run"));
        Assert.That(engineeringMessage, Does.Contain("missing_evidence=correlation-id"));
        Assert.That(engineeringMessage, Does.Not.Contain("generic-message-must-not-be-sent"));
        Assert.That(outbox.GetById(genericEntry.EventId)!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
        Assert.That(outbox.GetById(engineeringEntry.EventId)!.Status, Is.EqualTo(QChatOwnerEventStatus.Delivered));
    });
}
~~~

Add using System.Linq; only if the project does not provide implicit global usings.

- [ ] **Step 2: Verify the RED state**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter 'FullyQualifiedName~QChatOwnerEventDispatcherTests' -v:minimal
~~~

Expected: compilation fails because QChatOwnerEventRequest has no EngineeringReply parameter.

- [ ] **Step 3: Add optional persistence and conditional formatter selection**

Append the final parameter to the two records in QChatOwnerEventOutbox.cs:

~~~csharp
// QChatOwnerEventRequest final parameter
QChatOwnerEngineeringReply? EngineeringReply = null);

// QChatOwnerEventEntry final parameter, after LastError
QChatOwnerEngineeringReply? EngineeringReply = null);
~~~

Keep Message required for all events. Immediately after the existing message validation in Enqueue, add:

~~~csharp
if (request.EngineeringReply is not null)
    ValidateRequired(request.EngineeringReply.Facts, nameof(request.EngineeringReply.Facts));
~~~

When constructing QChatOwnerEventEntry, append this named argument after LastError: null:

~~~csharp
EngineeringReply: request.EngineeringReply
~~~

Extend IsValidLoadedEntry with this final condition, so legacy JSONL events without the optional property remain valid while blank typed facts are rejected:

~~~csharp
&& (entry.EngineeringReply is null ||
    !string.IsNullOrWhiteSpace(entry.EngineeringReply.Facts));
~~~

In QChatOwnerEventDispatcher.cs, replace the unconditional QChatCommandPersonaFormatter.Format call with:

~~~csharp
string formattedMessage = entry.EngineeringReply is { } engineeringReply
    ? QChatOwnerEngineeringReplyFormatter.Format(
        entry.AgentId,
        QChatSenderRole.Owner,
        engineeringReply)
    : QChatCommandPersonaFormatter.Format(
        entry.AgentId,
        QChatSenderRole.Owner,
        entry.Message);
~~~

Do not alter the serializer, retry handling, delivery status, generic text, or existing publisher call sites.

- [ ] **Step 4: Verify the GREEN state**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter 'FullyQualifiedName~QChatOwnerEngineeringReplyFormatterTests|FullyQualifiedName~QChatOwnerEventDispatcherTests' -v:minimal
~~~

Expected: typed messages include exact facts, verification, and failure text; generic messages retain the existing command-persona lead.

- [ ] **Step 5: Run affected owner-event regressions**

Run:

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter 'FullyQualifiedName~QChatOwnerEvent|FullyQualifiedName~OwnerQChatEvents|FullyQualifiedName~QChatPeriodicUpdateFlushesDueOwnerEvents|FullyQualifiedName~QChatReconnectFlushesDueOwnerEvents' -v:minimal
~~~

Expected: selected outbox, dispatcher, publisher, and service event tests pass.

- [ ] **Step 6: Commit the integration**

~~~powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventOutbox.cs sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs Tests/Alife.Test.QChat/QChatOwnerEventDispatcherTests.cs
git commit -m "feat: route engineering owner events through persona formatter"
~~~

### Task 3: Verify the completed change

**Files:**
- Verify: the five files listed in File Structure.

- [ ] **Step 1: Run the complete QChat project suite**

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal
~~~

Expected: no test failures. Record warnings separately from pass/fail evidence.

- [ ] **Step 2: Run solution verification and patch checks**

~~~powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Alife.slnx --no-restore --no-build -v:minimal
git diff master...HEAD --check
~~~

Expected: solution tests and whitespace check pass. If --no-build reports a missing output from another project, rerun the solution command without --no-build and record that fresh result.

- [ ] **Step 3: Review final scope**

Run:

~~~powershell
git diff master...HEAD -- sources/Alife.Function/Alife.Function.QChat/QChatOwnerEngineeringReply.cs sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventOutbox.cs sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs Tests/Alife.Test.QChat/QChatOwnerEngineeringReplyFormatterTests.cs Tests/Alife.Test.QChat/QChatOwnerEventDispatcherTests.cs
~~~

Expected: the diff contains only the typed contract, optional persisted payload, conditional dispatch, and direct tests. If verification changes no documentation, do not create an extra commit.
