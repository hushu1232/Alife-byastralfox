# Local Persona-Aware QChat Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give XiaYu and Mixu isolated local persona memory, scoped expressive address records, and concise persona-aware C# QQ feedback without changing authorization or protocol behavior.

**Architecture:** The memory provider uses a fixed agent-to-file map. Existing scoped `QChatUserProfileService` supplies local labels, while a small feedback context reaches only C# command and task feedback formatters. Full persona and real relationship records remain in ignored `Storage`.

**Tech Stack:** .NET 9, C#, NUnit, Semantic Kernel `ChatHistory`, OneBot/QChat, local JSON and Markdown.

---

### Task 1: Fixed Mixu persona memory

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatPersonaMemoryContextProvider.cs`
- Modify: `Tests/Alife.Test.QChat/QChatPersonaMemoryContextProviderTests.cs`

- [ ] **Step 1: Write the failing isolation test**

```csharp
[Test]
public void TrySeed_MixuReadsOnlyTheFixedMixuCharacterDirectory()
{
    WriteProfileForCharacter("夏羽", "xiayu-private-marker");
    WriteProfileForCharacter("咪绪", "mixu-private-marker");
    QChatPersonaMemoryContextProvider provider = new(storageRoot);
    QChatAgentIdentity mixu = QChatAgentIdentityRegistry.CreateDefault().ResolveByAgentId("mixu")!;
    ChatHistory history = [];

    Assert.That(provider.TrySeed(history, mixu), Is.True);
    string seeded = string.Join("\n", history.Select(message => message.Content));
    Assert.That(seeded, Does.Contain("mixu-private-marker"));
    Assert.That(seeded, Does.Not.Contain("xiayu-private-marker"));
}
```

- [ ] **Step 2: Verify RED**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~TrySeed_MixuReadsOnlyTheFixedMixuCharacterDirectory" --no-restore -v:minimal`

Expected: fail because only `xiayu` is currently accepted.

- [ ] **Step 3: Implement a fixed registry**

```csharp
sealed record PersonaProfileDefinition(string CharacterRelativePath, string FileName);

static readonly IReadOnlyDictionary<string, PersonaProfileDefinition> ProfileDefinitions =
    new Dictionary<string, PersonaProfileDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["xiayu"] = new("Character/夏羽", "夏羽-角色背景.md"),
        ["mixu"] = new("Character/咪绪", "咪绪-角色背景.md")
    };
```

`TrySeed` resolves only `identity.AgentId`; `TryReadApprovedProfile` receives the resolved definition and combines only `storageRoot`, `CharacterRelativePath`, and `FileName`. Retain current limits, containment and reparse checks, private markers, disclosure cache, and fail-closed errors.

- [ ] **Step 4: Verify GREEN and commit**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~QChatPersonaMemoryContextProviderTests" --no-restore -v:minimal`

Expected: all provider tests pass.

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatPersonaMemoryContextProvider.cs Tests/Alife.Test.QChat/QChatPersonaMemoryContextProviderTests.cs
git commit -m "feat(qchat): load Mixu persona from local memory"
```

### Task 2: Presentation-only feedback templates

**Files:**

- Create: `sources/Alife.Function/Alife.Function.QChat/QChatPersonaFeedbackContext.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatCommandPersonaFormatter.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatTaskFeedbackFormatter.cs`
- Modify: `Tests/Alife.Test.QChat/QChatCommandPersonaFormatterTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatTaskFeedbackFormatterTests.cs`

- [ ] **Step 1: Write failing formatter tests**

```csharp
[Test]
public void FormatForMixuPredecessorKeepsCommandBodyAndUsesRespectfulLead()
{
    QChatPersonaFeedbackContext context = new("mixu", QChatSenderRole.PrivateGuest, "前辈", "predecessor");
    string formatted = QChatCommandPersonaFormatter.Format(context, "diagnostic=ready");

    Assert.That(formatted, Does.Contain("前辈"));
    Assert.That(formatted, Does.Contain("diagnostic=ready"));
}

[Test]
public void FormatTaskFeedbackForMixuMotherKeepsFileAndFailureDetail()
{
    QChatPersonaFeedbackContext context = new("mixu", QChatSenderRole.PrivateGuest, "妈妈", "mother");
    string formatted = QChatTaskFeedbackFormatter.Format(
        new QChatTaskFeedbackContext(QChatTaskFeedbackKind.Failed, "qq.file_upload", "report.txt", 42, "gateway-timeout"), context);

    Assert.That(formatted, Does.Contain("妈妈"));
    Assert.That(formatted, Does.Contain("report.txt"));
    Assert.That(formatted, Does.Contain("gateway-timeout"));
}
```

- [ ] **Step 2: Verify RED**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~QChatCommandPersonaFormatterTests|FullyQualifiedName~QChatTaskFeedbackFormatterTests" --no-restore -v:minimal`

Expected: fail because the context and overloads do not exist.

- [ ] **Step 3: Implement the context and overloads**

```csharp
public sealed record QChatPersonaFeedbackContext(
    string? AgentId,
    QChatSenderRole SenderRole,
    string? PreferredAddress = null,
    string? RelationshipLabel = null);
```

Keep existing formatter overloads as compatibility wrappers. New overloads prepend a compact lead to the unchanged factual body. Use “术术” for XiaYu owner feedback; for Mixu use “妈妈”, “主人”, “前辈”, or a neutral lead based only on `PreferredAddress`. Do not append a mandatory `喵` suffix. Preserve all file names, ids, action statuses, details, and denial meaning; never format CQ/XML, URLs, command arguments, logs, diagnostics, audits, or model-authored text.

- [ ] **Step 4: Verify GREEN and commit**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~QChatCommandPersonaFormatterTests|FullyQualifiedName~QChatTaskFeedbackFormatterTests" --no-restore -v:minimal`

Expected: existing and new formatter tests pass.

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatPersonaFeedbackContext.cs sources/Alife.Function/Alife.Function.QChat/QChatCommandPersonaFormatter.cs sources/Alife.Function/Alife.Function.QChat/QChatTaskFeedbackFormatter.cs Tests/Alife.Test.QChat/QChatCommandPersonaFormatterTests.cs Tests/Alife.Test.QChat/QChatTaskFeedbackFormatterTests.cs
git commit -m "feat(qchat): personalize safe system feedback"
```

### Task 3: Wire scoped labels only into eligible feedback paths

**Files:**

- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`
- Modify: `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs`
- Modify: `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- Modify: `Tests/Alife.Test.QChat/QChatOwnerEventDispatcherTests.cs`

- [ ] **Step 1: Write failing scope and authority tests**

```csharp
profiles.SetProfile("mixu", 3340947887, new QChatUserProfile(
    UserId: 2001,
    PreferredNickname: "前辈",
    RelationshipLabel: "predecessor",
    Source: "local-owner-profile",
    Confidence: 1f));

Assert.That(mixuFeedback, Does.Contain("前辈"));
Assert.That(xiaYuFeedback, Does.Not.Contain("前辈"));
Assert.That(predecessorInbound.SenderRole, Is.EqualTo(QChatSenderRole.PrivateGuest));
Assert.That(ownerOnlyResult.Executed, Is.False);
```

- [ ] **Step 2: Verify RED**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~Mixu.*Predecessor|FullyQualifiedName~Scoped.*Feedback|FullyQualifiedName~QChatOwnerEventDispatcherTests" --no-restore -v:minimal`

Expected: feedback call sites cannot yet pass a scoped recipient context.

- [ ] **Step 3: Add one QChatService context builder**

```csharp
QChatPersonaFeedbackContext CreateFeedbackContext(QChatSenderRole senderRole, long userId, long botId)
{
    QChatConfig config = Configuration ?? new QChatConfig();
    string agentId = ResolveCurrentAgentId(config);
    profileRuntimeServices.UserProfiles.TryGetProfile(agentId, botId, userId, out QChatUserProfile? profile);
    string address = ResolvePreferredAddress(config, userId, null, agentId, botId);
    return new QChatPersonaFeedbackContext(agentId, senderRole, address, profile?.RelationshipLabel);
}
```

Pass it only to command response, task progress, task success, and task failure formatters. Owner-event dispatch uses `entry.AgentId`, `QChatSenderRole.Owner`, and the agent profile owner address. Do not alter `SendTextOrMediaMessageAsync`, XML tools, model output, CQ segments, audit writers, or authorization methods.

- [ ] **Step 4: Verify GREEN and commit**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~QChatServiceAdapterTests|FullyQualifiedName~QChatOwnerEventDispatcherTests|FullyQualifiedName~QChatCapabilityPolicyTests|FullyQualifiedName~QChatUserProfileServiceTests" --no-restore -v:minimal`

Expected: labels remain presentation data and owner-only protections remain unchanged.

```powershell
git add sources/Alife.Function/Alife.Function.QChat/QChatService.cs sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs Tests/Alife.Test.QChat/QChatOwnerEventDispatcherTests.cs
git commit -m "feat(qchat): apply recipient-aware system feedback"
```

### Task 4: Install private local Mixu records

**Files:**

- Create locally only: `Storage/Character/咪绪/Memory/Persona/咪绪-角色背景.md`
- Modify locally only: `Storage/AgentWorkspace/qchat-user-profiles.json`

- [ ] **Step 1: Write the approved Mixu background locally**

Create the ignored document with `status: approved_for_local_runtime_loading`. Copy the owner-approved background from this conversation, including identity, origin story, expressive relationships, natural catgirl wording, and the rule that labels grant no permissions. Never stage it.

- [ ] **Step 2: Add only Mixu-scoped address records**

Each ignored JSON entry uses `AgentId: "mixu"`, Mixu's bot id, `PreferredNickname`, `RelationshipLabel`, `Source: "local-owner-profile"`, and `Confidence: 1.0`. Do not add owner identity, permission scope, tool grant, or XiaYu-scoped data.

- [ ] **Step 3: Verify local-only boundaries**

Run:

```powershell
git check-ignore -v -- Storage/Character/咪绪/Memory/Persona/咪绪-角色背景.md
git check-ignore -v -- Storage/AgentWorkspace/qchat-user-profiles.json
git status --short --ignored=matching Storage/Character/咪绪 Storage/AgentWorkspace/qchat-user-profiles.json
```

Expected: both files are ignored and no private content is staged.

### Task 5: Final verification and review

**Files:**

- Verify: `Tests/Alife.Test.QChat/Alife.Test.QChat.csproj`

- [ ] **Step 1: Build QChat tests**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" build Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore -v:minimal`

Expected: `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 2: Run focused regressions**

Run: `& "C:\Users\hu shu\.dotnet\dotnet.exe" test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --filter "FullyQualifiedName~QChatPersonaMemoryContextProviderTests|FullyQualifiedName~QChatCommandPersonaFormatterTests|FullyQualifiedName~QChatTaskFeedbackFormatterTests|FullyQualifiedName~QChatUserProfileServiceTests|FullyQualifiedName~QChatCapabilityPolicyTests|FullyQualifiedName~LoadedXiayuPersonaProfileDoesNotReachVoiceSynthesis|FullyQualifiedName~FragmentedXiayuPersonaProfileDoesNotSynthesizeSecondProtectedVoiceRun|FullyQualifiedName~Mixu" --no-restore --no-build -v:minimal`

Expected: zero failed tests; persona isolation and presentation-only labels are proven.

- [ ] **Step 3: Verify data boundaries and request read-only review**

Run:

```powershell
git diff --check
git status --short --branch
git check-ignore -v -- Storage/Character/咪绪/Memory/Persona/咪绪-角色背景.md
git check-ignore -v -- Storage/AgentWorkspace/qchat-user-profiles.json
```

Expected: no whitespace error and no staged Storage content. Review fixed paths, cross-character isolation, scoped labels, unchanged authority, protocol-string preservation, and text plus voice disclosure protection.
