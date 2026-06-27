# QChat Intent Gating Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local intent gate so QChat natural-language command paths use keyword recall plus semantic confirmation before executing deterministic actions.

**Architecture:** Add a focused `QChatIntentClassifier` to classify recall, file-upload, and allowlist intents from cleaned user-authored text. Then migrate the three live-bug paths to use it while preserving exact slash commands and hard permission gates.

**Tech Stack:** C#/.NET, NUnit, existing `Alife.Function.QChat` service and `Tests/Alife.Test.QChat` adapter tests.

---

## File Structure

- Create `sources/Alife.Function/Alife.Function.QChat/QChatIntentClassifier.cs`
  - Owns `QChatIntentKind`, `QChatIntentTargetKind`, `QChatIntentInput`, `QChatIntentDecision`, and local intent classification helpers.
- Create `Tests/Alife.Test.QChat/QChatIntentClassifierTests.cs`
  - Fast unit tests for recall, file-upload, and allowlist intent decisions.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs`
  - Keep exact commands unchanged. Replace narrow recall keyword check with intent classifier helper.
- Modify `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Use intent decisions in owner recall, group existing-file upload, and owner allowlist natural-language paths.
  - Ensure deterministic action success messages are grounded in actual execution.
- Modify `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs`
  - Update recall tests to cover "撤了吧" and meta discussion rejection.
- Modify `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
  - Add integration tests for natural recall, file-upload false positive regression, and allowlist natural language.

---

### Task 1: Add Local Intent Classifier

**Files:**
- Create: `sources/Alife.Function/Alife.Function.QChat/QChatIntentClassifier.cs`
- Create: `Tests/Alife.Test.QChat/QChatIntentClassifierTests.cs`

- [ ] **Step 1: Write failing classifier tests**

Create `Tests/Alife.Test.QChat/QChatIntentClassifierTests.cs`:

```csharp
using Alife.Function.QChat;
using NUnit.Framework;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatIntentClassifierTests
{
    [TestCase("撤了吧")]
    [TestCase("把那条撤了")]
    [TestCase("撤你刚才那句")]
    [TestCase("删掉刚才那条")]
    public void RecallIntentConfirmsNaturalOwnerCommands(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyRecall(
            QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.RecallMessage));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetKind, Is.EqualTo(QChatIntentTargetKind.RecentBotMessage));
            Assert.That(decision.HasNegation, Is.False);
            Assert.That(decision.IsMetaDiscussion, Is.False);
        });
    }

    [TestCase("他是不是不会撤回")]
    [TestCase("不要撤回，我只是解释")]
    [TestCase("为什么撤回失败")]
    [TestCase("能不能撤回")]
    public void RecallIntentRejectsMetaDiscussionAndNegation(string text)
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyRecall(
            QChatIntentInput.FromText(text));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.RecallMessage));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.HasNegation || decision.IsMetaDiscussion, Is.True);
        });
    }

    [Test]
    public void FileUploadIntentRejectsForwardImageMetadataFalsePositive()
    {
        QChatIntentInput input = new(
            PlainText: "",
            ReadableText: """
                          # 转发消息内容 (ID: 7653692629493460645)
                          ## 1094950020(QQ用户)：
                          [图片: https://multimedia.nt.qq.com.cn/download?appid=1407&fileid=abc]
                          ## 1094950020(QQ用户)：
                          输入群主就会出现这个
                          """,
            RawMessage: "[CQ:forward,id=7653692629493460645]",
            HasReply: false,
            ReplyMessageId: null);

        QChatIntentDecision decision = QChatIntentClassifier.ClassifyFileUpload(input);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.GroupFileUpload));
            Assert.That(decision.IsCandidate, Is.True);
            Assert.That(decision.IsConfirmed, Is.False);
            Assert.That(decision.Reason, Does.Contain("metadata"));
        });
    }

    [Test]
    public void AllowlistIntentParsesCurrentGroupAdd()
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyAllowlist(
            QChatIntentInput.FromText("把这个群加入白名单"),
            currentGroupId: 1072509877);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Kind, Is.EqualTo(QChatIntentKind.AllowlistUpdate));
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetKind, Is.EqualTo(QChatIntentTargetKind.ExplicitGroup));
            Assert.That(decision.TargetId, Is.EqualTo(1072509877));
            Assert.That(decision.TargetText, Is.EqualTo("group:add"));
        });
    }

    [Test]
    public void AllowlistIntentParsesRawToolText()
    {
        QChatIntentDecision decision = QChatIntentClassifier.ClassifyAllowlist(
            QChatIntentInput.FromText("qchat_allowlist_update target=\"group\" action=\"add\" id=\"1072509877\""),
            currentGroupId: 0);

        Assert.Multiple(() =>
        {
            Assert.That(decision.IsConfirmed, Is.True);
            Assert.That(decision.TargetId, Is.EqualTo(1072509877));
            Assert.That(decision.TargetText, Is.EqualTo("group:add"));
        });
    }
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter QChatIntentClassifierTests
```

Expected: FAIL because `QChatIntentClassifier`, `QChatIntentInput`, and intent records do not exist.

- [ ] **Step 3: Add classifier implementation**

Create `sources/Alife.Function/Alife.Function.QChat/QChatIntentClassifier.cs`:

```csharp
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Alife.Function.QChat;

public enum QChatIntentKind
{
    None,
    RecallMessage,
    GroupFileUpload,
    PrivateFileUpload,
    AllowlistUpdate,
    Poke,
    QuietMode,
    GroupWake
}

public enum QChatIntentTargetKind
{
    None,
    CurrentSession,
    RepliedMessage,
    RecentBotMessage,
    TextMatch,
    ExplicitGroup,
    ExplicitUser,
    ExplicitFile
}

public sealed record QChatIntentInput(
    string PlainText,
    string ReadableText,
    string RawMessage,
    bool HasReply,
    long? ReplyMessageId)
{
    public static QChatIntentInput FromText(string text)
    {
        string value = text ?? string.Empty;
        return new QChatIntentInput(value, value, value, false, null);
    }
}

public sealed record QChatIntentDecision(
    QChatIntentKind Kind,
    bool IsCandidate,
    bool IsConfirmed,
    double Confidence,
    QChatIntentTargetKind TargetKind,
    string? TargetText,
    long? TargetId,
    string? FilePath,
    bool HasNegation,
    bool IsMetaDiscussion,
    string Reason);

public static class QChatIntentClassifier
{
    public static QChatIntentDecision ClassifyRecall(QChatIntentInput input)
    {
        string text = Merge(input.PlainText, input.ReadableText);
        bool candidate = ContainsAny(text, "撤", "撤回", "收回", "删掉", "删除");
        if (candidate == false)
            return None(QChatIntentKind.RecallMessage, "no recall keyword");

        bool negation = ContainsAny(text, "不要撤", "别撤", "不用撤", "不要删除", "别删除");
        bool meta = ContainsAny(text, "是不是", "会不会", "能不能", "为什么", "怎么", "失败", "不会撤回");
        bool command = ContainsAny(text, "撤了", "撤回", "收回", "删掉", "删除", "撤你", "撤刚才", "撤上一", "把那条撤", "把这条撤");
        bool confirmed = command && negation == false && meta == false;
        QChatIntentTargetKind target = input.HasReply
            ? QChatIntentTargetKind.RepliedMessage
            : QChatIntentTargetKind.RecentBotMessage;

        return new QChatIntentDecision(
            QChatIntentKind.RecallMessage,
            true,
            confirmed,
            confirmed ? 0.9 : 0.35,
            confirmed ? target : QChatIntentTargetKind.None,
            null,
            input.ReplyMessageId,
            null,
            negation,
            meta,
            confirmed ? "confirmed recall command" : "recall keyword is not an execution command");
    }

    public static QChatIntentDecision ClassifyFileUpload(QChatIntentInput input)
    {
        string commandText = input.PlainText;
        string allText = Merge(input.PlainText, input.ReadableText, input.RawMessage);
        bool candidate = ContainsAny(allText, "发", "发送", "传", "上传", "send", "upload", "file", "文件", "群");
        if (candidate == false)
            return None(QChatIntentKind.GroupFileUpload, "no file-upload keyword");

        bool metadataOnly = string.IsNullOrWhiteSpace(commandText) ||
                            (ContainsAny(input.RawMessage, "[CQ:forward", "[CQ:image") &&
                             ContainsAny(input.ReadableText, "fileid=", "转发消息内容", "图片:"));
        bool confirmed = metadataOnly == false &&
                         ContainsAny(commandText, "发", "发送", "传", "上传", "send", "upload") &&
                         ContainsAny(commandText, "文件", ".c", "file", "hello_world") &&
                         ContainsAny(commandText, "群", "群文件", "这里", "当前群", "group");

        return new QChatIntentDecision(
            QChatIntentKind.GroupFileUpload,
            true,
            confirmed,
            confirmed ? 0.86 : 0.2,
            confirmed ? QChatIntentTargetKind.CurrentSession : QChatIntentTargetKind.None,
            null,
            null,
            ExtractWindowsPath(commandText),
            false,
            false,
            confirmed ? "confirmed explicit file upload request" : "file-upload keywords came from metadata or incomplete command");
    }

    public static QChatIntentDecision ClassifyAllowlist(QChatIntentInput input, long currentGroupId)
    {
        string text = Merge(input.PlainText, input.ReadableText);
        bool candidate = ContainsAny(text, "白名单", "allowlist", "qchat_allowlist_update");
        if (candidate == false)
            return None(QChatIntentKind.AllowlistUpdate, "no allowlist keyword");

        string action = ContainsAny(text, "移除", "删除", "remove") ? "remove" : "add";
        long id = ExtractFirstId(text);
        if (id == 0 && ContainsAny(text, "这个群", "本群", "当前群"))
            id = currentGroupId;
        bool confirmed = id > 0 && ContainsAny(text, "群", "group", "target=\"group\"");

        return new QChatIntentDecision(
            QChatIntentKind.AllowlistUpdate,
            true,
            confirmed,
            confirmed ? 0.88 : 0.3,
            confirmed ? QChatIntentTargetKind.ExplicitGroup : QChatIntentTargetKind.None,
            confirmed ? $"group:{action}" : null,
            confirmed ? id : null,
            null,
            false,
            false,
            confirmed ? "confirmed group allowlist update" : "allowlist target is missing");
    }

    static QChatIntentDecision None(QChatIntentKind kind, string reason) =>
        new(kind, false, false, 0, QChatIntentTargetKind.None, null, null, null, false, false, reason);

    static string Merge(params string[] values) => string.Join('\n', values.Where(v => string.IsNullOrWhiteSpace(v) == false));

    static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    static long ExtractFirstId(string text)
    {
        Match match = Regex.Match(text, @"(?<!\d)([1-9]\d{5,12})(?!\d)");
        return match.Success && long.TryParse(match.Groups[1].Value, out long value) ? value : 0;
    }

    static string? ExtractWindowsPath(string text)
    {
        Match match = Regex.Match(text, @"[A-Za-z]:[\\/][^\r\n""<>|?*]+");
        return match.Success ? match.Value.Trim() : null;
    }
}
```

- [ ] **Step 4: Run tests and verify they pass**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter QChatIntentClassifierTests
```

Expected: PASS for all classifier tests.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatIntentClassifier.cs Tests/Alife.Test.QChat/QChatIntentClassifierTests.cs
git commit -m "feat: add qchat intent classifier"
```

---

### Task 2: Migrate Owner Recall To Intent Gate

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs:68-71`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:4416-4517`
- Modify: `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs:250-258`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Update failing unit tests**

Change `IsRecallCommandDetectsRecallKeywords` in `Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs` to:

```csharp
[TestCase("撤回刚才那条", true)]
[TestCase("收回上一条", true)]
[TestCase("删除刚刚发的", true)]
[TestCase("撤了吧", true)]
[TestCase("把那条撤了", true)]
[TestCase("不要撤回，我只是解释", false)]
[TestCase("他是不是不会撤回", false)]
[TestCase("hello", false)]
public void IsRecallCommandDetectsRecallIntent(string text, bool expected)
{
    Assert.That(QChatOwnerCommandService.IsRecallCommand(text), Is.EqualTo(expected));
}
```

- [ ] **Step 2: Add failing service tests**

Add near the existing recall tests in `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`:

```csharp
[Test]
public async Task OwnerGroupRecallNaturalShortCommandDeletesLatestGroupBotMessage()
{
    FakeOneBotRuntime runtime = new() { NextMessageId = 9000 };
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        EnableBalancedTextStreaming = false
    });
    await service.SendChatAsync("group", 3001, "message to recall");
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        Interlocked.Increment(ref dispatchCount);
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        GroupId = 3001,
        RawMessage = "撤了吧"
    });

    await WaitUntilAsync(() => runtime.DeletedMessages.Count == 1, TimeSpan.FromSeconds(2));
    Assert.That(runtime.DeletedMessages, Is.EqualTo(new[] { 9000L }));
    Assert.That(dispatchCount, Is.EqualTo(0));
}

[Test]
public async Task OwnerGroupRecallMetaDiscussionDoesNotDeleteOrBypassModel()
{
    FakeOneBotRuntime runtime = new() { NextMessageId = 9000 };
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        EnableBalancedTextStreaming = false
    });
    await service.SendChatAsync("group", 3001, "message to keep");
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        Interlocked.Increment(ref dispatchCount);
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        GroupId = 3001,
        RawMessage = "他是不是不会撤回"
    });

    await Task.Delay(300);
    Assert.That(runtime.DeletedMessages, Is.Empty);
    Assert.That(dispatchCount, Is.GreaterThanOrEqualTo(1));
}
```

- [ ] **Step 3: Run tests and verify they fail**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatOwnerCommandServiceTests|OwnerGroupRecall"
```

Expected: FAIL because "撤了吧" is not yet handled and meta discussion may still match broad recall keywords.

- [ ] **Step 4: Implement recall intent gate**

Change `QChatOwnerCommandService.IsRecallCommand` to:

```csharp
public static bool IsRecallCommand(string text)
{
    return QChatIntentClassifier.ClassifyRecall(QChatIntentInput.FromText(text)).IsConfirmed;
}
```

In `TryHandleOwnerRecallCommandAsync`, build the input and use the decision:

```csharp
string plainText = OneBotSegment.GetPlainText(messageEvent.RawMessage);
long? replyId = messageEvent.GetReplyId();
QChatIntentDecision decision = QChatIntentClassifier.ClassifyRecall(new QChatIntentInput(
    PlainText: plainText,
    ReadableText: readable,
    RawMessage: messageEvent.RawMessage,
    HasReply: replyId.HasValue,
    ReplyMessageId: replyId));
WriteQChatDiagnostic("qchat-intent-decision", "QChat recall intent was evaluated.", new {
    messageEvent.MessageType,
    messageEvent.UserId,
    messageEvent.GroupId,
    decision.Kind,
    decision.IsCandidate,
    decision.IsConfirmed,
    decision.TargetKind,
    decision.Reason
});
if (decision.IsConfirmed == false)
    return false;
```

Keep the existing reply-message and recent-message deletion code below this decision.

- [ ] **Step 5: Run recall tests and verify pass**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatOwnerCommandServiceTests|OwnerGroupRecall"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatOwnerCommandService.cs sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatOwnerCommandServiceTests.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "fix: gate qchat recall intent"
```

---

### Task 3: Gate Existing Group File Upload And Stop Metadata False Positives

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:4711-4842`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Add failing forwarded-message regression test**

Add near existing group file command tests:

```csharp
[Test]
public async Task ForwardedImageMetadataDoesNotTriggerExistingGroupFileUploadApproval()
{
    string previousStorage = Alife.Platform.AlifePath.StorageFolderPath;
    string storage = Path.Combine(TestContext.CurrentContext.WorkDirectory, "qchat-intent-forward-file");
    Directory.CreateDirectory(Path.Combine(storage, "AgentWorkspace"));
    Alife.Platform.AlifePath.SetStorageFolderPath(storage, persist: false);
    try
    {
        string output = Path.Combine(Environment.CurrentDirectory, "output");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "hello_world.c"), "int main(void) { return 0; }");

        FakeOneBotRuntime runtime = new();
        runtime.ForwardMessages["forward-fileid"] =
        [
            new OneBotForwardMessage
            {
                Sender = new OneBotSender { UserId = 1094950020, Nickname = "QQ用户" },
                Message = JsonSerializer.Deserialize<JsonElement>("""
                    [{"type":"image","data":{"url":"https://multimedia.nt.qq.com.cn/download?fileid=abc","file":"x.jpg"}}]
                    """)
            },
            new OneBotForwardMessage
            {
                Sender = new OneBotSender { UserId = 1094950020, Nickname = "QQ用户" },
                Message = JsonSerializer.Deserialize<JsonElement>("""
                    [{"type":"text","data":{"text":"输入群主就会出现这个"}}]
                    """)
            }
        ];
        QChatService service = CreateStartedService(runtime, new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            EnableBalancedTextStreaming = false
        });
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
            GroupId = 3001,
            RawMessage = "[CQ:forward,id=forward-fileid]"
        });

        await Task.Delay(500);
        Assert.That(runtime.GroupFiles, Is.Empty);
        Assert.That(runtime.GroupMessages.Any(message => message.Message.Contains("Owner confirmation required", StringComparison.Ordinal)), Is.False);
        Assert.That(dispatchCount, Is.GreaterThanOrEqualTo(1));
    }
    finally
    {
        Alife.Platform.AlifePath.SetStorageFolderPath(previousStorage, persist: false);
    }
}
```

If this file does not already import `System.Text.Json`, add:

```csharp
using System.Text.Json;
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter ForwardedImageMetadataDoesNotTriggerExistingGroupFileUploadApproval
```

Expected: FAIL because current code can route metadata-triggered text into the existing file upload gateway.

- [ ] **Step 3: Implement file-upload intent gate**

In `TryHandleOwnerDeterministicFileCommandAsync`, keep a command text that separates raw plain text from readable text:

```csharp
string plainText = OneBotSegment.GetPlainText(messageEvent.RawMessage);
string text = $"{plainText}\n{readable}";
```

In `TryHandleExistingGroupFileSendCommandAsync`, classify before checking candidates:

```csharp
QChatIntentDecision decision = QChatIntentClassifier.ClassifyFileUpload(new QChatIntentInput(
    PlainText: OneBotSegment.GetPlainText(messageEvent.RawMessage),
    ReadableText: text,
    RawMessage: messageEvent.RawMessage,
    HasReply: messageEvent.GetReplyId().HasValue,
    ReplyMessageId: messageEvent.GetReplyId()));
WriteQChatDiagnostic("qchat-intent-decision", "QChat group file upload intent was evaluated.", new {
    messageEvent.GroupId,
    messageEvent.UserId,
    senderRole,
    decision.Kind,
    decision.IsCandidate,
    decision.IsConfirmed,
    decision.Reason
});
if (decision.IsConfirmed == false)
    return false;
```

Reject non-owner group file upload requests before calling `QGroupFile` so no hidden approval request is created and no raw approval prompt is leaked to the group:

```csharp
if (senderRole != QChatSenderRole.Owner)
{
    WriteQChatDiagnostic("qchat-group-existing-file-command-rejected", "Non-owner group file-send intent was rejected before QQ file gateway.", new {
        messageEvent.GroupId,
        messageEvent.UserId,
        senderRole,
        decision.Reason
    });
    return true;
}
```

Keep owner-facing failures visible in the current session.

Update `NonOwnerGroupSendThisFileCommandRequiresOwnerApprovalBeforeUpload` to reflect the new safety policy:

```csharp
await Task.Delay(300);
Assert.Multiple(() =>
{
    Assert.That(dispatchCount, Is.Zero);
    Assert.That(runtime.GroupFiles, Is.Empty);
    Assert.That(runtime.GroupMessages, Is.Empty);
    Assert.That(approvals.GetRequest(1), Is.Null);
});
```

- [ ] **Step 4: Run regression test and existing file tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "ForwardedImageMetadataDoesNotTriggerExistingGroupFileUploadApproval|OwnerGroupSendThisFileCommandUploadsRecentHelloWorldWithoutModelDispatch|NonOwnerGroupSendThisFileCommandRequiresOwnerApprovalBeforeUpload"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "fix: gate qchat file upload intent"
```

---

### Task 4: Add Owner Natural-Language Allowlist Updates

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Add failing allowlist integration tests**

Add near existing allowlist tests:

```csharp
[Test]
public async Task OwnerGroupNaturalAllowlistCommandAddsCurrentGroupBeforeModelDispatch()
{
    FakeOneBotRuntime runtime = new();
    QChatConfig config = new()
    {
        BotId = 999,
        OwnerId = 1001,
        AllowedGroupIds = "867165927",
        EnableBalancedTextStreaming = false
    };
    QChatService service = CreateStartedService(runtime, config);
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        Interlocked.Increment(ref dispatchCount);
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        GroupId = 1072509877,
        RawMessage = "把这个群加入白名单"
    });

    await WaitUntilAsync(() => config.AllowedGroupIds.Contains("1072509877", StringComparison.Ordinal));
    Assert.That(config.AllowedGroupIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        Is.EquivalentTo(new[] { "867165927", "1072509877" }));
    Assert.That(dispatchCount, Is.EqualTo(0));
    Assert.That(runtime.GroupMessages.Single().Message, Does.Contain("1072509877"));
}

[Test]
public async Task OwnerPrivateRawAllowlistToolTextAddsExplicitGroupBeforeModelDispatch()
{
    FakeOneBotRuntime runtime = new();
    QChatConfig config = new()
    {
        BotId = 999,
        OwnerId = 1001,
        AllowedGroupIds = "867165927",
        EnableBalancedTextStreaming = false
    };
    QChatService service = CreateStartedService(runtime, config);
    int dispatchCount = 0;
    service.InboundChatDispatcher = _ =>
    {
        Interlocked.Increment(ref dispatchCount);
        return Task.CompletedTask;
    };

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        RawMessage = "qchat_allowlist_update target=\"group\" action=\"add\" id=\"1072509877\""
    });

    await WaitUntilAsync(() => config.AllowedGroupIds.Contains("1072509877", StringComparison.Ordinal));
    Assert.That(dispatchCount, Is.EqualTo(0));
    Assert.That(runtime.PrivateMessages.Single().Message, Does.Contain("1072509877"));
}
```

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "OwnerGroupNaturalAllowlistCommandAddsCurrentGroupBeforeModelDispatch|OwnerPrivateRawAllowlistToolTextAddsExplicitGroupBeforeModelDispatch"
```

Expected: FAIL because natural allowlist commands currently dispatch to the model.

- [ ] **Step 3: Add handler to owner command chain**

In the owner command chain near the existing diagnostics/status handlers, add:

```csharp
context => TryHandleOwnerAllowlistIntentCommandAsync(context.MessageEvent, context.SenderRole, context.ReadableMessage),
```

Implement:

```csharp
async Task<bool> TryHandleOwnerAllowlistIntentCommandAsync(
    OneBotMessageEvent messageEvent,
    QChatSenderRole senderRole,
    string readable)
{
    if (senderRole != QChatSenderRole.Owner)
        return false;

    long currentGroupId = messageEvent.MessageType == OneBotMessageType.Group ? messageEvent.GroupId : 0;
    QChatIntentDecision decision = QChatIntentClassifier.ClassifyAllowlist(new QChatIntentInput(
        PlainText: OneBotSegment.GetPlainText(messageEvent.RawMessage),
        ReadableText: readable,
        RawMessage: messageEvent.RawMessage,
        HasReply: messageEvent.GetReplyId().HasValue,
        ReplyMessageId: messageEvent.GetReplyId()),
        currentGroupId);
    WriteQChatDiagnostic("qchat-intent-decision", "QChat allowlist intent was evaluated.", new {
        messageEvent.MessageType,
        messageEvent.UserId,
        messageEvent.GroupId,
        decision.Kind,
        decision.IsCandidate,
        decision.IsConfirmed,
        decision.TargetText,
        decision.TargetId,
        decision.Reason
    });
    if (decision.IsConfirmed == false || decision.TargetId is not long id)
        return false;

    string[] parts = (decision.TargetText ?? "group:add").Split(':', 2);
    string target = parts[0];
    string action = parts.Length > 1 ? parts[1] : "add";
    await QChatAllowlistUpdate(target, action, id);
    return true;
}
```

- [ ] **Step 4: Run allowlist tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "Allowlist"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git commit -m "feat: add qchat allowlist intent commands"
```

---

### Task 5: Full QChat Verification

**Files:**
- No source changes unless tests expose a regression.

- [ ] **Step 1: Run focused QChat tests**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "QChatIntentClassifierTests|QChatOwnerCommandServiceTests|OwnerGroupRecall|ForwardedImageMetadata|Allowlist"
```

Expected: PASS.

- [ ] **Step 2: Run full QChat test project**

Run:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj
```

Expected: PASS, with the same skipped live tests as before.

- [ ] **Step 3: Check worktree**

Run:

```powershell
git status --short
```

Expected: only pre-existing unrelated changes remain, or no changes if all task commits were made.

- [ ] **Step 4: Summarize live deployment note**

Do not restart the live bot unless the user asks. Report that code is implemented and tested, but the running process still needs a controlled restart or plugin reload before live QQ behavior changes.
