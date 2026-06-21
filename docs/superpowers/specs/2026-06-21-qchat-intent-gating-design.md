# QChat Intent Gating Design

## Background

Two live QQ incidents exposed the same design flaw in QChat deterministic command paths:

- A Mio group forwarded message triggered `qq.group_file_upload` approval even though the forwarded content only contained an image and text about "群主". The current file-send path combined words from forwarded-message formatting, image `fileid` metadata, and a default `hello_world.c` candidate.
- A XiaYu group recall request failed to behave naturally. Owner messages such as "撤了吧" and "把那条消息撤了" were not handled by the deterministic recall path because the keyword list only recognized "撤回", "收回", "删掉", and "删除". The model later claimed a recall succeeded before the real recall tool succeeded.

The root issue is that several paths treat keyword presence as enough evidence to trigger command handling. Keyword matching should only recall candidates. Execution needs a second, structured intent judgment and must remain separated from permissions and tool results.

## Goal

Replace direct keyword-triggered QChat command handling with a shared "keyword candidate + semantic intent gate + permission execution + result-grounded reply" pattern.

This should make natural owner requests easier to use while reducing false positives from CQ metadata, forwarded messages, image URLs, and casual discussion.

## Non-Goals

- Do not replace explicit slash commands such as `/qchat`, `/approve 1`, `/deny 1`, `/status`, or `/tasks`; these remain exact command paths.
- Do not let semantic judgment grant authority. Account identity, owner checks, allowlists, risk levels, and approval state remain hard gates.
- Do not call a large model for every message. The first implementation should use local deterministic intent classification.
- Do not refactor unrelated QChat architecture.

## Architecture

Introduce a focused QChat intent layer before natural-language deterministic commands:

1. Normalize input into separate command-relevant text, readable text, CQ metadata, reply context, sender role, and session route.
2. Use existing keyword checks only to create cheap intent candidates.
3. Run a local semantic classifier for each candidate to decide whether the message is an actual command, a meta discussion, a negation, or an unsafe/ambiguous request.
4. Execute only confirmed intents through existing permission and gateway code.
5. Reply only from actual tool execution results; the model must not be able to claim success for recall, upload, allowlist, or other deterministic tasks unless a tool result exists.

The first classifier should be local C# code, not model calls. It should expose simple records that tests can assert without starting a QChat service.

## Core Types

Create a small intent module under `Alife.Function.QChat`:

```csharp
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
```

The classifier can grow by intent, but the first pass should cover three high-priority paths:

- `RecallMessage`
- `GroupFileUpload`
- `AllowlistUpdate`

## Text Sources

Intent classification must distinguish:

- raw user plain text,
- readable reply text,
- forwarded-message body text,
- CQ metadata,
- image URLs and `fileid` strings,
- bot-generated diagnostic text.

For safety-sensitive command paths, command intent should prefer plain user text and reply context. CQ metadata and image/file URLs may supply evidence that a media object exists, but they must not supply imperative words such as "发", "file", "group", or "upload".

This is the key fix for the Mio false upload approval.

## Recall Intent

Recall intent should confirm common owner phrases:

- "撤了"
- "撤了吧"
- "把那条撤了"
- "把这条撤了吧"
- "撤刚才那条"
- "撤你刚才那句"
- "删掉刚才那条"
- "收回上一句"

It should reject meta discussion and negation:

- "他是不是不会撤回"
- "不要撤回，我只是解释"
- "为什么撤回失败"
- "能不能撤回"

If the message replies to a bot message, target the replied message. If there is no reply but the phrase points to "刚才/上一条/那条", target the recent bot message in the current session. Later work may add text-match targeting, but the first implementation should at least not claim success unless the actual target was deleted.

## File Upload Intent

File upload intent must require all of the following:

- explicit upload/send wording in user-authored plain text,
- an explicit file reference or previously recorded pending file context,
- an explicit target such as current group, a group id, or private user,
- owner authority or existing permission-gateway approval.

It must reject:

- forwarded messages where trigger words come from "转发消息内容",
- image `fileid`, URLs, and CQ metadata as command words,
- casual discussion about files,
- non-owner group member attempts that lack an owner-approved workflow.

When a non-owner group member appears to request a high-risk upload, QChat should not send raw approval prompts into the public group. It should either ignore the request or notify the owner via owner outbox/private notification.

## Allowlist Intent

Allowlist intent should support owner natural language:

- "把这个群加入白名单"
- "1072509877 加入群白名单"
- "把 1072509877 从群白名单移除"
- `qchat_allowlist_update target="group" action="add" id="1072509877"`

The deterministic handler should execute only for the configured owner account. Non-owner messages and language claiming to be owner must remain ordinary chat.

When the target is "这个群" in group context, use the current group id. In private context, require an explicit group id.

## Result-Grounded Replies

For deterministic actions, user-visible success text must be sent only after the action result succeeds.

Examples:

- Recall success: "撤回成功：<preview>"
- Recall failure: "撤回失败：<short NapCat error>; messageId=<id>"
- Upload success: "已上传到群文件：<name>"
- Upload blocked: private owner notification or concise owner-only failure, not a public raw permission prompt.
- Allowlist success: include the resulting allowlist status.

The model may continue normal conversation after a deterministic command is ignored, but it must not claim a deterministic action succeeded unless the action tool produced success.

## Testing Strategy

Add tests before implementation:

- Intent classifier tests for recall command variants, negations, and meta discussion.
- Intent classifier tests proving forwarded image `fileid` and "群主" do not confirm file-upload intent.
- Service adapter tests proving "撤了吧" triggers recall and does not dispatch to the model.
- Service adapter tests proving failed NapCat recall returns a failure result and does not claim success.
- Service adapter tests proving owner natural-language allowlist update changes `AllowedGroupIds`.
- Regression tests for explicit `/qchat`, `/approve`, and `/deny` paths to ensure exact commands remain unchanged.

## Rollout Order

1. Add the intent classifier and focused unit tests.
2. Migrate owner recall handling to use the classifier.
3. Migrate existing group file-send command handling to reject CQ/forward metadata false positives and stop public approval leaks.
4. Add deterministic owner allowlist natural-language handling.
5. Migrate poke, quiet mode, and group wake decisions after the high-risk paths are stable.

## Safety Requirements

- Semantic intent detection is never an authorization source.
- High-risk actions remain owner-only or approval-gated.
- Non-owner public groups must not receive raw internal approval strings.
- Live bot availability should not require restarts during design and test work; code deployment can be scheduled separately.
- Logs should record candidate, confirmed, reason, target, and executed status for each intent decision.

## Acceptance Criteria

- "撤了吧" from the owner in a group recalls the latest bot message in that group or reports a concrete failure.
- "他是不是不会撤回" does not trigger a recall.
- A forwarded message containing image URLs, `fileid`, "转发消息内容", and "群主" does not trigger file upload approval.
- Owner "把这个群加入白名单" in a group updates that bot's `AllowedGroupIds`.
- Bot does not claim recall/upload/allowlist success without a corresponding successful deterministic action result.
- Existing exact commands continue to work.
