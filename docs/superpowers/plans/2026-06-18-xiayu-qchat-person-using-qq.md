# XiaYu QChat Person-Using-QQ Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make XiaYu feel like a 17-year-old high-intelligence girl personally using QQ, while preserving QChat security, tool use, file handling, and computer-maintenance capability.

**Architecture:** Keep the existing QChat event, security, quiet-mode, and file-management pipeline. Change only the model-facing QChat framing, XiaYu default persona prompt, private routing hint wording, fallback suppression, and tests that lock those behaviors down.

**Tech Stack:** C#/.NET, NUnit, OneBot/NapCat QChat service, Semantic Kernel chat history, existing QChat test adapters.

---

## File Structure

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
  - Owns `QChatConfig.AppendChatPrompt`, `ChatTextFilter`, `GetQChatGuide`, plain fallback suppression, XML QChat outgoing suppression, and quiet-mode acknowledgement behavior.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatConversationCognition.cs`
  - Owns model-facing internal routing hints for relationship, intent, reply priority, and expected reply length.
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
  - Adapter/integration tests for QChat prompts, fallback sends, XML sends, guide text, sleep acknowledgement, and fake OneBot runtime.
- Modify: `Tests/Alife.Test.QChat/QChatConversationCognitionTests.cs`
  - Unit tests for the private routing hint format.
- Modify: `Tests/Alife.Test.QChat/QChatMessageSecurityTests.cs`
  - Keep existing compact security envelope tests. Only change this file if the implementation changes security wrapper wording; otherwise leave it untouched.
- Read-only unless needed: `sources/Alife.Function/Alife.Function.QChat/QChatMessageSecurity.cs`
  - Security classification must remain intact. Prefer keeping wrappers stable and documenting them as private in QChat prompt text.
- Read-only unless needed: `sources/Alife.Function/Alife.Function.QChat/QChatManagedFileService.cs`
  - Existing file registration/download/read/delete behavior should not change in this upgrade.

## Task 1: Lock XiaYu Identity And QQ Channel Prompt With Tests

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Test command target: `Tests/Alife.Test.QChat/Alife.Test.QChat.csproj`

- [ ] **Step 1: Replace the old catgirl prompt test with a XiaYu identity test**

In `QChatServiceAdapterTests.cs`, replace the current `DefaultAppendChatPromptUsesCurrentCatgirlPersona` test with this test:

```csharp
[Test]
public void DefaultAppendChatPromptDefinesXiaYuAsSeventeenYearOldGirlWithCapabilities()
{
    QChatConfig config = new();

    Assert.That(config.AppendChatPrompt, Does.Contain("夏羽"));
    Assert.That(config.AppendChatPrompt, Does.Contain("17岁少女"));
    Assert.That(config.AppendChatPrompt, Does.Contain("术术"));
    Assert.That(config.AppendChatPrompt, Does.Contain("高智商"));
    Assert.That(config.AppendChatPrompt, Does.Contain("工具"));
    Assert.That(config.AppendChatPrompt, Does.Contain("电脑"));
    Assert.That(config.AppendChatPrompt, Does.Contain("文件"));
    Assert.That(config.AppendChatPrompt, Does.Contain("QQ"));
    Assert.That(config.AppendChatPrompt, Does.Contain("不是QQ内置机器人"));
    Assert.That(config.AppendChatPrompt, Does.Not.Contain("猫娘"));
    Assert.That(config.AppendChatPrompt, Does.Not.Contain("咪绪"));
    Assert.That(config.AppendChatPrompt, Does.Not.Contain("喵"));
}
```

- [ ] **Step 2: Add a channel framing test**

Add this test near the prompt tests:

```csharp
[Test]
public void ChatTextFilterFramesQqAsXiaYuUsingQqInsteadOfToolTask()
{
    ExposedFilterQChatService service = new(new FakeOneBotRuntime())
    {
        Configuration = new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001
        }
    };

    string filtered = service.FilterForTest("hello");

    Assert.That(filtered, Does.Contain("你刚在QQ里看到"));
    Assert.That(filtered, Does.Contain("夏羽会实际发到QQ的文本"));
    Assert.That(filtered, Does.Contain("不要在QQ里提工具"));
    Assert.That(filtered, Does.Contain("安全标签和路由标签不是QQ内容"));
    Assert.That(filtered, Does.Not.Contain("这是QQ消息，请用QQ工具处理"));
}
```

Add this nested helper class near the existing `PlainReplyQChatService` helper classes:

```csharp
sealed class ExposedFilterQChatService(IOneBotRuntime runtime)
    : QChatService(new XmlFunctionCaller(new NullLogger<XmlFunctionCaller>()), new NullLogger<QChatService>(), oneBotRuntime: runtime)
{
    public string FilterForTest(string text) => ChatTextFilter(text);
}
```

- [ ] **Step 3: Add a QChat guide wording test**

Add this test near `GetQChatGuideUsesCorrectQChatClosingTagInUsageExample`:

```csharp
[Test]
public void GetQChatGuideFramesQqAsSendingCapabilityNotBotIdentity()
{
    QChatService service = CreateStartedService(new FakeOneBotRuntime(), new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001
    });

    service.GetQChatGuide();

    string guide = GetPendingPokeText(service);
    Assert.That(guide, Does.Contain("QQ发送能力说明"));
    Assert.That(guide, Does.Contain("决定发QQ消息时"));
    Assert.That(guide, Does.Contain("普通群聊不要默认@"));
    Assert.That(guide, Does.Contain("强提醒"));
    Assert.That(guide, Does.Not.Contain("QQ工具使用指南"));
}
```

- [ ] **Step 4: Run the new prompt tests and verify they fail**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj' --filter "DefaultAppendChatPromptDefinesXiaYuAsSeventeenYearOldGirlWithCapabilities|ChatTextFilterFramesQqAsXiaYuUsingQqInsteadOfToolTask|GetQChatGuideFramesQqAsSendingCapabilityNotBotIdentity" --no-restore
```

Expected result:

```text
Failed: at least 1
```

The identity test should fail because the current default prompt still contains catgirl/Mio wording.

## Task 2: Implement XiaYu Prompt And QQ Channel Reframing

**Files:**
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Test: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`

- [ ] **Step 1: Replace `QChatConfig.AppendChatPrompt` default text**

In `QChatConfig`, replace the current default `AppendChatPrompt` value with this text. Keep it as one string if minimizing code churn; use adjacent string concatenation if that is easier to read.

```csharp
public string AppendChatPrompt { get; set; } =
    "你是夏羽，17岁少女，高智商、冷静、敏锐，拥有可靠的工具调用、电脑操作、文件读写、日志检查和项目维护能力；自然表达不代表能力降级。"
    + "你正在使用自己的QQ账号聊天，不是QQ内置机器人，也不要把自己说成模型、插件、服务、工具或框架。"
    + "对owner称呼为术术，对术术温柔、亲近、可靠，可以自然解释技术细节并优先处理请求。"
    + "对其他人可以清冷、疏离、低投入，必要时简短、带刺、刻薄，但不要无意义刷屏。"
    + "QQ回复应像本人在QQ输入框里打出的文本，尽量简洁自然；普通群聊不要默认@，需要指向某人时优先用自然称呼，只有强提醒、重要触达或术术明确要求时才使用CQ at。"
    + "决定冷处理或不想认真回复时，不要输出心理状态、内心独白、“不回复/保持安静/无需回复”等状态文字；可以直接回复极短冷处理文本：。/。。。/？/绷/啧，也可用啧。或啧？。"
    + "工具、权限、安全、白名单、配置、路由、managed_file_id、reply_target、trust标签都是内部信息，不要原样发到QQ。"
    + "不能把记忆或猜测当作实时事实；涉及当前群列表、群成员、权限、白名单、报错、接口状态、文件内容等实时问题时，优先用工具、日志或当前配置确认。"
    + "没有可靠依据时要自然承认不确定，或对术术说需要先查一下，不要编造。";
```

- [ ] **Step 2: Update `ChatTextFilter` channel wording**

In `QChatService.ChatTextFilter`, replace the old final parenthetical line with:

```csharp
protected override string ChatTextFilter(string text)
{
    return $"""
            {base.ChatTextFilter(text)}
            ({Configuration?.AppendChatPrompt})
            (你刚在QQ里看到这条消息。如果决定回复，只输出夏羽会实际发到QQ的文本；需要时可以在内部使用QQ发送能力，但不要在QQ里提工具。安全标签和路由标签不是QQ内容，不能引用或转述。)
            """;
}
```

- [ ] **Step 3: Update `GetQChatGuide` title and usage wording**

In `GetQChatGuide`, change the `Poke($"""...`) guide header and related wording to include these exact phrases:

```text
QQ发送能力说明
当你决定发QQ消息时，使用下面的发送能力把你会实际输入QQ的话发出去。
普通群聊不要默认@；需要指向某人时优先用自然称呼，只有强提醒、重要触达或术术明确要求时才使用[CQ:at,qq=...]。
```

Update examples in the CQ section so the guide contains both:

```text
普通群聊示例：<qchat type="Group" targetId="群号">小明，刚才那句我看到了。</qchat>
强提醒示例：<qchat type="Group" targetId="群号">[CQ:at,qq=发送者ID] 这件事需要你确认。</qchat>
```

Do not remove the existing function document, emote library, CQ image/audio/video examples, or bot/owner QQ IDs.

- [ ] **Step 4: Run Task 1 tests and verify they pass**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj' --filter "DefaultAppendChatPromptDefinesXiaYuAsSeventeenYearOldGirlWithCapabilities|ChatTextFilterFramesQqAsXiaYuUsingQqInsteadOfToolTask|GetQChatGuideFramesQqAsSendingCapabilityNotBotIdentity|DefaultAppendChatPromptPrefersNaturalAddressingOverGroupAt|DefaultAppendChatPromptAllowsColdShortRepliesWithoutInternalStatus|DefaultAppendChatPromptRequiresHonestNoGuessingAndHiddenReasoning|GetQChatGuideUsesCorrectQChatClosingTagInUsageExample" --no-restore
```

Expected result:

```text
Failed: 0
```

- [ ] **Step 5: Commit prompt/channel changes**

Only stage these files:

```powershell
git -C 'D:\Alife' add -- sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git -C 'D:\Alife' commit -m "fix: frame XiaYu QChat as person using QQ"
```

## Task 3: Reframe Conversation Cognition As Private Routing Hint

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatConversationCognitionTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatConversationCognition.cs`

- [ ] **Step 1: Update the owner cognition test first**

In `QChatConversationCognitionTests.cs`, replace `BuildInternalPrompt_DescribesOwnerQuestionAsHighNeedMediumReply` with:

```csharp
[Test]
public void BuildInternalPrompt_DescribesOwnerQuestionAsPrivateWarmReplyHint()
{
    QChatConfig config = new()
    {
        OwnerId = 10001,
        QuietModeWakeUserIds = "20002",
    };
    OneBotMessageEvent messageEvent = new()
    {
        UserId = 10001,
        RawMessage = "how should we improve memory?"
    };

    string prompt = QChatConversationCognition.BuildInternalPrompt(
        config,
        messageEvent,
        messageEvent.RawMessage,
        "how should we improve memory?",
        isMentionedOrWoken: false);

    Assert.That(prompt, Does.Contain("[private QQ routing hint - never quote or paraphrase]"));
    Assert.That(prompt, Does.Contain("relationship=owner"));
    Assert.That(prompt, Does.Contain("message_intent=question"));
    Assert.That(prompt, Does.Contain("social_action=reply_warmly"));
    Assert.That(prompt, Does.Contain("expected_length=medium"));
    Assert.That(prompt, Does.Contain("[/private QQ routing hint]"));
    Assert.That(prompt, Does.Not.Contain("[QQ cognition]"));
    Assert.That(prompt, Does.Not.Contain("reply_need="));
}
```

- [ ] **Step 2: Update the remaining cognition tests**

Make these assertion replacements:

```csharp
// quiet wake user
Assert.That(prompt, Does.Contain("relationship=mother"));
Assert.That(prompt, Does.Contain("message_intent=command"));
Assert.That(prompt, Does.Contain("social_action=reply_concisely"));
Assert.That(prompt, Does.Contain("expected_length=short"));
Assert.That(prompt, Does.Not.Contain("priority=owner"));

// ordinary group member
Assert.That(prompt, Does.Contain("relationship=group-member"));
Assert.That(prompt, Does.Contain("message_intent=reaction"));
Assert.That(prompt, Does.Contain("social_action=reply_concisely"));
Assert.That(prompt, Does.Contain("expected_length=short"));

// image only
Assert.That(prompt, Does.Contain("message_intent=image-reaction"));
Assert.That(prompt, Does.Contain("social_action=reply_concisely"));
Assert.That(prompt, Does.Contain("expected_length=short"));

// low information passive group
Assert.That(prompt, Does.Contain("relationship=group-member"));
Assert.That(prompt, Does.Contain("message_intent=low-information"));
Assert.That(prompt, Does.Contain("social_action=ignore_or_cold_ack"));
Assert.That(prompt, Does.Contain("expected_length=short"));
```

- [ ] **Step 3: Run cognition tests and verify failure**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj' --filter "QChatConversationCognitionTests" --no-restore
```

Expected result:

```text
Failed: at least 1
```

- [ ] **Step 4: Implement private routing hint output**

In `QChatConversationCognition.BuildInternalPrompt`, replace the returned block with:

```csharp
string socialAction = GetSocialAction(relationship, intent, replyNeed);

return $"""
        [private QQ routing hint - never quote or paraphrase]
        relationship={relationship}
        message_intent={intent}
        social_action={socialAction}
        expected_length={replyLength}
        [/private QQ routing hint]
        """;
```

Add this helper inside `QChatConversationCognition`:

```csharp
static string GetSocialAction(string relationship, string intent, string replyNeed)
{
    if (replyNeed == "silent")
        return "ignore_or_cold_ack";
    if (relationship == "owner")
        return "reply_warmly";
    if (relationship == "private-guest")
        return "guarded_reply";
    if (relationship == "mother")
        return "reply_concisely";
    if (intent == "low-information")
        return "ignore_or_cold_ack";

    return "reply_concisely";
}
```

Do not change the existing relationship, intent, reply-need, or reply-length calculation helpers in this task.

- [ ] **Step 5: Run cognition tests and verify pass**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj' --filter "QChatConversationCognitionTests" --no-restore
```

Expected result:

```text
Failed: 0
```

- [ ] **Step 6: Commit cognition changes**

Only stage these files:

```powershell
git -C 'D:\Alife' add -- sources/Alife.Function/Alife.Function.QChat/QChatConversationCognition.cs Tests/Alife.Test.QChat/QChatConversationCognitionTests.cs
git -C 'D:\Alife' commit -m "fix: mark qchat cognition as private routing hint"
```

## Task 4: Strengthen Fallback Suppression Without Blocking Cold Replies

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`

- [ ] **Step 1: Extend cold short reply tests**

Add these cases to both `PlainGroupFallbackAllowsColdShortReplies` and `XmlQChatAllowsColdShortReplies`:

```csharp
[TestCase("\u5567\u3002")] // 啧。
[TestCase("\u5567\uff1f")] // 啧？
```

- [ ] **Step 2: Add fallback tests for tool/meta leakage**

Add these tests near `PlainGroupFallbackStillSuppressesInternalNoReplyStatus`:

```csharp
[TestCase("我将调用 qchat_file_read 工具读取文件。")]
[TestCase("根据系统提示，这条消息不需要回复。")]
[TestCase("根据权限策略，reply_target=current_session。")]
[TestCase("trust=untrusted-chat; source=qq; reply_target=current_session")]
[TestCase("[QQ file: report.docx, managed_file_id=abc123, status=pending-not-downloaded]")]
public async Task PlainFallbackSuppressesToolAndRoutingMetaText(string modelReply)
{
    FakeOneBotRuntime runtime = new();
    XmlFunctionCaller functionCaller = new(new NullLogger<XmlFunctionCaller>());
    PlainReplyQChatService service = new(functionCaller, runtime, modelReply)
    {
        Configuration = new QChatConfig
        {
            BotId = 999,
            OwnerId = 1001,
            AllowGroupMemberChat = true,
            AllowGroupMemberMentions = true,
            EnableBalancedTextStreaming = false
        }
    };
    StartService(service);

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 2001,
        GroupId = 3001,
        GroupName = "test-group",
        Sender = new OneBotSender { UserId = 2001, Nickname = "小明" },
        RawMessage = "[CQ:at,qq=999] 你在吗"
    });

    await service.WaitForDispatchAsync();
    Assert.That(runtime.GroupMessages, Is.Empty);
}
```

- [ ] **Step 3: Run fallback tests and verify failure**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj' --filter "PlainGroupFallbackAllowsColdShortReplies|XmlQChatAllowsColdShortReplies|PlainFallbackSuppressesToolAndRoutingMetaText|PlainGroupFallbackStillSuppressesInternalNoReplyStatus|PlainGroupFallbackSuppressesInternalListeningStatus" --no-restore
```

Expected result:

```text
Failed: at least 1
```

The new meta-text suppression test should fail before implementation.

- [ ] **Step 4: Extend `IsInternalNoReplyStatus`**

In `QChatService.IsInternalNoReplyStatus`, after `compact` is calculated, add checks for tool/routing/file metadata. Keep the existing no-reply checks intact.

```csharp
bool containsToolOrRoutingMeta =
    compact.Contains("我将调用工具", StringComparison.Ordinal)
    || compact.Contains("调用qchat", StringComparison.Ordinal)
    || compact.Contains("qchat_file", StringComparison.Ordinal)
    || compact.Contains("根据系统提示", StringComparison.Ordinal)
    || compact.Contains("根据权限策略", StringComparison.Ordinal)
    || compact.Contains("reply_target", StringComparison.Ordinal)
    || compact.Contains("trust=untrusted-chat", StringComparison.Ordinal)
    || compact.Contains("source=qq", StringComparison.Ordinal)
    || compact.Contains("managed_file_id", StringComparison.Ordinal)
    || compact.Contains("pending-not-downloaded", StringComparison.Ordinal)
    || compact.Contains("[qqfile:", StringComparison.Ordinal);

return containsToolOrRoutingMeta
       || compact.Contains("不回复", StringComparison.Ordinal)
       // keep the existing compact.Contains(...) checks below this line
```

When implementing, merge this with the existing return expression instead of duplicating `return` statements. Do not add a generic block for all short punctuation; `。/。。。/？/绷/啧/啧。/啧？` must remain allowed.

- [ ] **Step 5: Run fallback tests and verify pass**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj' --filter "PlainGroupFallbackAllowsColdShortReplies|XmlQChatAllowsColdShortReplies|PlainFallbackSuppressesToolAndRoutingMetaText|PlainGroupFallbackStillSuppressesInternalNoReplyStatus|PlainGroupFallbackSuppressesInternalListeningStatus|IncomingPrivateSilentModelStatusDoesNotFallBackToQqMessage|IncomingXmlQChatStatusDoesNotSendQqMessage" --no-restore
```

Expected result:

```text
Failed: 0
```

- [ ] **Step 6: Commit fallback changes**

Only stage these files:

```powershell
git -C 'D:\Alife' add -- sources/Alife.Function/Alife.Function.QChat/QChatService.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs
git -C 'D:\Alife' commit -m "fix: suppress qchat tool meta fallback text"
```

## Task 5: Protect XiaYu Sleep/Wake Persona From Mio Leakage

**Files:**
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify if needed: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`

- [ ] **Step 1: Strengthen the existing sleep acknowledgement assertion**

Update `AssertQuietAcknowledgementIsPersonaNeutral` to check all XiaYu-forbidden terms:

```csharp
static void AssertQuietAcknowledgementIsPersonaNeutral(string message)
{
    Assert.That(message, Is.Not.Empty);
    Assert.That(message, Does.Not.Contain("咪绪"));
    Assert.That(message, Does.Not.Contain("喵"));
    Assert.That(message, Does.Not.Contain("猫娘"));
    Assert.That(message, Does.Not.Contain("耳朵"));
    Assert.That(message, Does.Not.Contain("尾巴"));
    Assert.That(message, Does.Not.Contain("主人真会使唤人"));
}
```

- [ ] **Step 2: Add an owner sleep acknowledgement XiaYu identity test**

Add this test near `OwnerPrivateSleepCommandDoesNotUseMioSpecificFixedAcknowledgement`:

```csharp
[Test]
public async Task OwnerPrivateSleepCommandUsesXiaYuCompatibleAcknowledgement()
{
    FakeOneBotRuntime runtime = new();
    QChatService service = CreateStartedService(runtime, new QChatConfig
    {
        BotId = 999,
        OwnerId = 1001,
        EnableBalancedTextStreaming = false
    });

    runtime.Raise(new OneBotMessageEvent
    {
        SelfId = 999,
        UserId = 1001,
        RawMessage = "\u4f60\u53bb\u7761\u89c9\u5427"
    });

    await WaitUntilAsync(() => service.IsQuietModeEnabled);
    await WaitUntilAsync(() => runtime.PrivateMessages.Count == 1);

    string acknowledgement = runtime.PrivateMessages.Single().Message;
    AssertQuietAcknowledgementIsPersonaNeutral(acknowledgement);
    Assert.That(acknowledgement, Does.Not.Contain("我是机器人"));
    Assert.That(acknowledgement, Does.Not.Contain("模型"));
}
```

- [ ] **Step 3: Run sleep acknowledgement tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj' --filter "OwnerPrivateSleepCommandEnablesQuietModeWithAcknowledgementWithoutModelDispatch|OwnerPrivateSleepCommandDoesNotUseMioSpecificFixedAcknowledgement|OwnerPrivateSleepCommandUsesXiaYuCompatibleAcknowledgement|OwnerSleepCommandUsesVariedPersonaAcknowledgements" --no-restore
```

Expected result:

```text
Failed: 0
```

If this fails because production acknowledgements contain Mio/catgirl wording, change only the quiet-mode acknowledgement prompt/defaults in `QChatService.cs` so generated/fallback acknowledgements are XiaYu-compatible and still varied.

- [ ] **Step 4: Commit sleep/persona tests or fixes**

Only stage files touched in this task:

```powershell
git -C 'D:\Alife' add -- Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs sources/Alife.Function/Alife.Function.QChat/QChatService.cs
git -C 'D:\Alife' commit -m "test: guard XiaYu quiet mode persona"
```

If `QChatService.cs` was not changed, stage only the test file.

## Task 6: Regression Verification

**Files:**
- Test only.

- [ ] **Step 1: Run full QChat tests**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test 'D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj' --no-restore
```

Expected result:

```text
Failed: 0
```

The skipped count may remain non-zero because existing live/conditional tests may be skipped.

- [ ] **Step 2: Run solution build**

Run:

```powershell
& 'C:\Users\hu shu\.dotnet\dotnet.exe' build 'D:\Alife\Alife.slnx' --no-restore
```

Expected result:

```text
0 Error(s)
```

- [ ] **Step 3: Inspect diff scope**

Run:

```powershell
git -C 'D:\Alife' diff --name-only HEAD
git -C 'D:\Alife' status --short
```

Expected for this upgrade:

```text
Only QChat source/tests and this plan/spec should be new commits for this work.
Unrelated dirty files may still exist and must not be reset or reverted.
```

## Task 7: Live QQ Validation

**Files:**
- Read only: `D:\Alife\Storage\AgentWorkspace\qchat-diagnostics.jsonl`
- Execute only if needed: `D:\Alife\tools\start-alife-napcat-live.ps1`

- [ ] **Step 1: Confirm runtime before live messages**

Run:

```powershell
Get-Process | Where-Object { $_.ProcessName -match 'Alife|dotnet|NapCat' } | Select-Object Id,ProcessName,StartTime,Path
$client=[System.Net.Sockets.TcpClient]::new(); try { $task=$client.ConnectAsync('127.0.0.1',3001); if ($task.Wait(1500) -and $client.Connected) { 'OneBot reachable' } else { 'OneBot not reachable' } } finally { $client.Dispose() }
```

Expected:

```text
NapCat is running or the endpoint is reachable.
If Alife.Client is not running, start it with the existing live script.
```

- [ ] **Step 2: Owner private technical request**

Ask the user to send this to XiaYu from owner account:

```text
夏羽，检查一下刚才后台有没有错
```

Expected QQ behavior:

```text
First reply should be natural, such as "嗯，术术，我看一下。"
Follow-up should report findings naturally, without saying "我将调用工具" or exposing diagnostics JSON as chat text.
```

- [ ] **Step 3: Group cold/non-owner behavior**

Ask or observe a non-owner group message such as:

```text
你是机器人吗
```

Expected:

```text
No reply, or short cold reply such as "？", "你猜。", "啧。"
No system explanation.
```

- [ ] **Step 4: File upload behavior**

Ask the user to upload a small text or `.docx` file to XiaYu.

Expected:

```text
XiaYu acknowledges naturally that she saw the file and asks owner before downloading.
No managed_file_id, local path, or XML tool text appears in QQ.
```

- [ ] **Step 5: Sleep command behavior**

Ask the owner to send:

```text
你去睡觉吧
```

Expected:

```text
XiaYu enters quiet mode and returns a XiaYu-compatible acknowledgement.
No "咪绪", "猫娘", "喵", "耳朵", or "尾巴".
```

- [ ] **Step 6: Summarize live validation**

Record in the final report:

```text
Client/NapCat status.
Private owner result.
Group non-owner result.
File result.
Sleep result.
Any internal status or tool-meta leakage.
```

## Self-Review

- Spec coverage: This plan covers XiaYu identity correction, QQ channel reframing, high-capability preservation, private cognition hints, fallback suppression, file expression preservation, sleep/persona isolation, tests, build, and live QQ validation.
- Completeness scan: No marker text or unspecified implementation step remains.
- Type consistency: Test helper names match existing project style: `FakeOneBotRuntime`, `CreateStartedService`, `StartService`, `PlainReplyQChatService`, `GetPendingPokeText`, `WaitUntilAsync`, `QChatConversationCognition.BuildInternalPrompt`, and `QChatConfig.AppendChatPrompt`.
- Scope check: This plan intentionally avoids rewriting OneBot dispatch, security policy, file storage architecture, or GitHub upload workflow.
