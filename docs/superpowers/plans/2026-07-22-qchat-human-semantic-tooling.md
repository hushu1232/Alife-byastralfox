# QChat Human Conversation and Semantic Tooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce scripted QChat behavior while letting the model choose bounded, permission-gated reads by semantic intent rather than user-keyword triggers.

**Architecture:** Keep permission, routing, sending, and XML execution in C#. Replace full persona seeding with compact stable identity plus bounded persona reads. Append a concise safe scoped-read offer to the normal model route every eligible turn; only an exact marker response is intercepted, validated, read, and followed by a natural final reply. All non-marker responses continue through the existing XML tool route unchanged.

**Tech Stack:** .NET 9, C#, existing QChat XML caller, QChat prompt envelope, NUnit QChat tests.

---

## File structure

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatPersonaMemoryContextProvider.cs` — stop full profile seeding.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatScopedCapabilityTurnExecutor.cs` — bounded capability parsing, reading, and feedback.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatConversationCognition.cs` — factual routing hint only.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatPromptEnvelope.cs` — trusted/external distinction and cap.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs` — compact QChat static prompt, semantic scoped-read interception, contextual envelopes, address injection.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatExperienceSanitizer.cs` — retain only actual internal-leak cleanup.
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatReplyLayoutNormalizer.cs` — preserve one intentional ordinary line break.
- Modify: matching `Tests/Alife.Test.QChat/*Tests.cs` — regression coverage for every behavior.
- Modify: `docs/runbooks/alife-local-dual-account-production.md` — explain that complete persona files remain local and are read through bounded context only.
- Modify locally only: `Storage/account-a/Configuration/Alife.Function.QChat.QChatService.json` and `Storage/Character/夏羽/Memory/Persona/夏羽-角色背景.md` — align known legacy XiaYu text with the approved current profile; never stage either file.

**Route invariant:** Do not pass an offered scoped capability through the legacy text-only `ExecuteAsync` path before `DispatchStandardModelAsync`. That path returns a text-only reply even when no marker is selected and would bypass the established XML tool execution route. Instead, invoke the normal route first, then hand an exact marker to the scoped capability executor for bounded reading and feedback finalization.

### Task 1: Stop whole-persona injection and prove bounded access

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatPersonaMemoryContextProvider.cs:40-53`
- Modify: `Tests/Alife.Test.QChat/QChatPersonaMemoryContextProviderTests.cs`

- [ ] **Step 1: Write failing tests for no full persona seeding**

Add a test that writes an approved profile over 600 characters, calls `TrySeed`, and asserts the history contains no raw profile body while the provider still reads the approved document for `QChatPersonaFactProvider`.

```csharp
[Test]
public void TrySeed_DoesNotAppendWholePersonaDocumentToChatHistory()
{
    var provider = CreateProviderWithApprovedXiaYuProfile(longApprovedProfile);
    var history = new ChatHistory();

    Assert.That(provider.TrySeed(history, XiaYuIdentity), Is.True);
    Assert.That(history.Select(message => message.Content), Does.Not.Contain(longApprovedProfile));
    Assert.That(provider.TryReadApprovedProfile(XiaYuIdentity), Is.EqualTo(longApprovedProfile));
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatPersonaMemoryContextProviderTests"
```

Expected: failure because `TrySeed` currently adds the complete document to `ChatHistory`.

- [ ] **Step 3: Make `TrySeed` cache disclosure protection only**

Remove the `history.AddUserMessage` call. Keep validation, `CacheProtectedProfile(document)`, and the return value `true` when the approved document is available.

```csharp
CacheProtectedProfile(document);
return true;
```

- [ ] **Step 4: Run focused test and verify GREEN**

Run the Task 1 command. Expected: all selected tests pass.

### Task 2: Replace keyword-gated scoped reads with model-selected safe capabilities

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatScopedCapabilityTurnExecutor.cs:43-168`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:9395-9519`
- Modify: `Tests/Alife.Test.QChat/QChatScopedCapabilityTurnExecutorTests.cs`

- [ ] **Step 1: Write failing paraphrase tests**

Create a request whose text does not contain any old selector phrase but whose normal model response requests an offered scoped capability. Assert the marker is intercepted, the bounded read runs, and the final model call receives feedback.

```csharp
[Test]
public async Task ExecuteAsync_OffersApprovedPersonaReadWithoutKeywordGate()
{
    QChatScopedCapabilityTurnResult result = await executor.ExecuteAsync(
        Request(candidateText: "她平时会怎么叫人", hasApprovedPersona: true),
        RespondWith("[[qchat_capability:persona_speech_style]]"),
        CancellationToken.None);

    Assert.That(result.CapabilityOffered, Is.True);
    Assert.That(result.CapabilityRequested, Is.True);
    Assert.That(result.Feedback!.Capability, Is.EqualTo("persona_speech_style"));
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatScopedCapabilityTurnExecutorTests"
```

Expected: failure because no `persona_speech_style` capability is offered, parsed, or intercepted from the normal model route.

- [ ] **Step 3: Offer bounded reads by availability**

Replace keyword selection with an availability builder. When history exists, offer `current_conversation_context`; when an approved persona exists, offer the five explicit persona names: `persona_origin`, `persona_relationship`, `persona_speech_style`, `persona_behavior_boundary`, and `persona_confirmed_preference`.

The offer must list each name, bounded purpose, and privacy boundary. Extend the exact marker parser only to those names; map them in C# to `QChatPersonaFactCategory`. Reject unknown names and retain the single-read-per-turn rule. Remove the now-unused keyword selector dependency.

- [ ] **Step 4: Intercept markers from the normal model route**

Append the concise scoped-read offer to the normal model input before `ChatBot.ChatAsync`, without entering the legacy text-only response scope. If the normal response is an exact scoped marker, intercept it before QQ delivery, read the bounded data, and invoke the feedback pass. If the response is not a marker, return it unchanged so ordinary XML tools remain available. The feedback prompt must require one natural QQ reply and never emit marker/XML/protocol content to QQ.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the Task 2 command. Expected: old keyword cases and new paraphrase cases pass; unknown capability tests remain denied.

### Task 3: Reduce scripted turn context and distinguish trust

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatConversationCognition.cs:22-101`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatPromptEnvelope.cs:5-21`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:4158-4227,4621-4637`
- Modify: `Tests/Alife.Test.QChat/QChatConversationCognitionTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatPromptEnvelopeTests.cs`

- [ ] **Step 1: Write failing factual-context tests**

Assert cognition retains owner/non-owner, intent, mention/quiet eligibility but no longer contains `attachment=`, `desire=`, `jealousy=`, `emotional_distance=`, or `social_action=`.

```csharp
Assert.That(prompt, Does.Contain("relationship=owner"));
Assert.That(prompt, Does.Contain("message_intent=question"));
Assert.That(prompt, Does.Not.Contain("attachment="));
Assert.That(prompt, Does.Not.Contain("social_action="));
```

Add envelope tests that a trusted internal block has `trust=trusted-internal`, an external block has `trust=untrusted-external`, and oversized content is truncated to the supplied maximum.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatConversationCognitionTests|FullyQualifiedName~QChatPromptEnvelopeTests"
```

Expected: failure because current cognition emits personality actions and every envelope emits `untrusted=true`.

- [ ] **Step 3: Implement factual cognition and typed envelope**

Replace the persona style contract and social-action calculation with factual routing fields. Change `Wrap` to accept a trust enum and maximum length; truncate before rendering. In `BuildFormattedModelInput`, mark persona frame and character state trusted internal, while message, recall, image, and research evidence remain external. Only build address context when preferred address or address style is non-empty.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 3 command. Expected: factual routing remains available and no forced emotional/action label is present.

### Task 4: Simplify visible output without breaking safety

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatExperienceSanitizer.cs:33-93`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatReplyLayoutNormalizer.cs:20-78`
- Modify: `Tests/Alife.Test.QChat/QChatReplyLayoutNormalizerTests.cs`
- Modify: existing QChat visible-output tests that cover internal-label removal.

- [ ] **Step 1: Write failing natural-layout and sanitizer tests**

Add a test that two ordinary non-empty lines remain separated by one newline. Add a test that an ordinary sentence containing a non-routing word is not globally rewritten, while a model/route label is still removed.

```csharp
Assert.That(normalizer.Normalize("第一句\n第二句"), Is.EqualTo("第一句\n第二句"));
Assert.That(sanitizer.SanitizeOutgoing(config, OneBotMessageType.Private, 1, "那只猫在喵喵叫"), Does.Contain("喵喵"));
Assert.That(sanitizer.SanitizeOutgoing(config, OneBotMessageType.Private, 1, "私聊回复：你好"), Is.EqualTo("你好"));
```

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatReplyLayoutNormalizerTests|FullyQualifiedName~QChatVisible"
```

Expected: failure because ordinary lines are joined and broad persona replacement changes user-visible ordinary text.

- [ ] **Step 3: Keep only leak removal and preserve intentional rhythm**

Delete broad Xiayu word substitutions. Keep self-identification and routing-label removal. Update the normalizer to preserve up to one consecutive ordinary line break; collapse only runs of three or more short ordinary lines without punctuation into one line.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 4 command. Expected: internal labels remain hidden and natural two-line replies remain intact.

### Task 5: Unify static QChat guidance and validate the full path

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs:154-163,2834-2862,2887-2927`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `docs/runbooks/alife-local-dual-account-production.md`

- [ ] **Step 1: Write failing stable-prompt tests**

Assert the stable QChat prompt contains the compact delivery rule and current XiaYu profile but does not contain `先调用<GetQChatGuide/>`, `普通文字不会自动出现在 QQ`, XML-only wording, or a raw full persona document.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatServiceAdapterTests"
```

Expected: failure because the current static prompt contains guide/XML-only wording.

- [ ] **Step 3: Replace static wording with one delivery rule**

Use only: “面向 QQ 用户的内容必须通过当前会话发送能力交付；不要在 QQ 文本中解释内部工具、路由或权限。” Keep actual XML registration and C# execution policy unchanged. Keep the compact 19-year-old, polite-but-distant XiaYu defaults.

- [ ] **Step 4: Update the approved local XiaYu revision without committing it**

Before editing local state, require both files to match the known legacy revision. Update the local `AppendChatPrompt` to the current 19-year-old, warm-to-owner and polite-distant-to-others wording. Update the local persona Markdown to 19 years old and replace its universal no-period rule with the approved split: owner replies naturally omit final periods; non-owner replies may use restrained periods. Keep the full document local-only and do not stage `Storage/`.

- [ ] **Step 5: Run the full focused suite**

Run:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests/Alife.Test.QChat/Alife.Test.QChat.csproj --no-restore
git diff --check
```

Expected: QChat tests pass and whitespace check is empty.

- [ ] **Step 6: Commit**

```bash
git add sources/Alife.Function/Alife.Function.QChat Tests/Alife.Test.QChat docs/runbooks/alife-local-dual-account-production.md docs/superpowers/specs/2026-07-22-qchat-human-semantic-tooling-design.md docs/superpowers/plans/2026-07-22-qchat-human-semantic-tooling.md
git commit -m "fix(qchat): humanize prompt and tool reasoning"
```
