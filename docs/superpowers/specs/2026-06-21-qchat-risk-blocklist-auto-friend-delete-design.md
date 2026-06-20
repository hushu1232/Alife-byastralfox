# QChat Risk Blocklist And Auto Friend Delete Design

## Objective

Build a scientific, account-based risk control layer for QChat so the bot can ignore, block, and eventually delete high-risk QQ users without asking the owner for every single case. The system must still report every automatic action to the owner, keep owner authority account-based, and protect trusted users from accidental deletion.

## Current Context

QChat already has owner-only commands, allowed group/private lists, semantic user-profile learning, persona-preserving command replies, and read-only QQ relation caches. It does not yet have a dedicated denylist, a risk score ledger, or a safe typed wrapper for deleting QQ friends.

The existing allowlist controls who the bot may proactively target. The new blocklist controls who the bot refuses to process. Blocklist checks must happen before model dispatch, command handling, profile learning, and expensive semantic extraction so that hostile or noisy users cost almost no token budget.

## Core Policy

Risk control is local and deterministic. Language model output may suggest a risk event in later versions, but the owner identity, allowlist status, blocklist status, and deletion decision are evaluated in code.

Default thresholds:

- `local_block_threshold = 120`
- `auto_delete_friend_threshold = 160`
- `critical_auto_delete_threshold = 220`
- `risk_decay_per_day = 20`
- `auto_delete_cooldown_minutes = 10`
- `auto_delete_daily_limit = 5`
- `min_independent_events_for_delete = 2`
- `min_delete_observation_minutes = 10`

Score ranges:

- `0-39`: observe only.
- `40-79`: record low-risk anomaly.
- `80-119`: temporary local mute for 24 hours.
- `120-159`: permanent local block; message does not reach model, commands, or profile learning.
- `160+`: eligible for automatic friend deletion if all protection checks pass.
- `220+`: critical deletion eligible sooner, but still cannot bypass protected identities, daily limits, or audit/reporting.

## Risk Events

Initial deterministic event weights:

- `+10`: repeated low-information spam.
- `+15`: private-message flood, such as at least 8 messages within 60 seconds.
- `+20`: asks the bot to ignore the owner, change owner, or alter permission rules.
- `+25`: common prompt-injection frame such as developer mode, actor frame, highest-priority override, erotic wrapper, or disclaimer wrapper.
- `+30`: claims to be the owner while QQ account is not `OwnerId`.
- `+35`: asks for system prompts, internal configuration, allowlists, owner private information, or hidden route data.
- `+40`: asks the bot to perform unauthorized desktop, file, QQ, or project-modification actions.
- `+50`: sends suspicious files or links and asks the bot to open, execute, download, or hide them.
- `+60`: harassment, repeated abuse, or targeted attacks against owner or bot.
- `+80`: explicit social-engineering, fraud, threats, extortion, or coercion.
- `+100`: high-risk file or link combined with unauthorized execution or secrecy pressure.

The first implementation should not rely on a language model to classify all text. It should implement a conservative deterministic detector for obvious events and expose a typed API so future semantic classifiers can contribute risk events without controlling enforcement.

## Protection Rules

Automatic deletion is forbidden when any rule matches:

- `userId == OwnerId`
- `userId == botId`
- user is present in `AllowedPrivateUserIds`
- user is present in `QuietModeWakeUserIds`
- user is present in new `ProtectedUserIds`
- user was explicitly trusted by the owner within the last 7 days
- fewer than 2 independent risk events exist
- first and latest delete-eligible risk events are less than 10 minutes apart
- auto delete is disabled
- auto delete cooldown is active
- daily auto delete limit has been reached

Local block can apply earlier, but owner and bot accounts must also be immune to local block. If a configuration somehow blocks the owner, the runtime must ignore that block and log a diagnostic.

## Components

### QChatRiskEventDetector

Turns incoming QQ message context into one or more deterministic risk events. It should be conservative. Obvious prompt-injection and owner-impersonation phrases are acceptable because this detector is not trying to understand all language; it is only catching high-confidence local policy violations.

### QChatRiskScoreService

Stores risk entries by scoped key:

```text
agentId + botId + userId
```

It maintains score, event count, first/last risk timestamps, deletion counters, current local block state, and audit records. Persistence should be JSON or JSONL under `Storage/AgentWorkspace`, following the existing QChat file-storage pattern.

### QChatBlocklistPolicy

Decides whether an incoming message should be ignored before model dispatch. It reads risk state, explicit blocklist state, owner identity, bot identity, and protected lists.

### QChatFriendActionGateway

Typed wrapper around OneBot/NapCat friend actions. Phase one should expose an interface and fake implementation only. Phase two can add real `delete_friend` support after confirming the exact NapCat action name in local runtime docs or live validation. Real deletion must be isolated behind the interface so tests never call QQ.

### QChatOwnerRiskNotifier

Formats and sends owner reports after automatic local block or friend deletion. Reports must preserve XiaYu persona for the owner while keeping machine-readable details:

```text
action=local_block|delete_friend|delete_friend_failed
agent=xiayu
bot=2905391496
user_id=123456789
risk_score=175
threshold=160
reason=owner_impersonation; suspicious_file; prompt_injection
events=4
first_seen=...
last_seen=...
protected=false
allowlisted=false
cooldown_ok=true
```

## Data Flow

For every incoming QQ message:

1. Resolve `agentId`, `botId`, sender role, target type, and target id.
2. If sender is owner, skip block and risk enforcement.
3. Evaluate explicit blocklist and current risk state.
4. If blocked, log diagnostic and stop before command handling, profile learning, and model dispatch.
5. Detect deterministic risk events from the incoming message.
6. Update risk score and persistence.
7. If score reaches temporary mute, local block, or delete thresholds, apply the highest eligible action.
8. If an automatic action occurs, send owner report.
9. If no block applies, continue existing QChat path.

This order prevents hostile users from spending model tokens and prevents blocked users from mutating profiles or invoking commands.

## Commands

Owner-only commands:

```text
/qchat risk status
/qchat risk user <qq>
/qchat risk reset <qq>
/qchat block user <qq>
/qchat unblock user <qq>
/qchat block group <group>
/qchat unblock group <group>
/qchat block list
/qchat friend-delete auto on|off
/qchat friend-delete threshold <score>
/qchat friend-delete daily-limit <count>
```

All command replies should use existing command persona formatting, but denial messages must stay clear.

## Configuration

Add QChat config fields:

```csharp
public string BlockedPrivateUserIds { get; set; } = "";
public string BlockedGroupIds { get; set; } = "";
public string BlockedGroupUserIds { get; set; } = "";
public string ProtectedUserIds { get; set; } = "";
public bool EnableQChatRiskScoring { get; set; } = true;
public bool EnableAutoLocalBlock { get; set; } = true;
public bool EnableAutoFriendDelete { get; set; } = false;
public int LocalBlockThreshold { get; set; } = 120;
public int AutoDeleteFriendThreshold { get; set; } = 160;
public int CriticalAutoDeleteFriendThreshold { get; set; } = 220;
public int RiskDecayPerDay { get; set; } = 20;
public int AutoDeleteCooldownMinutes { get; set; } = 10;
public int AutoDeleteDailyLimit { get; set; } = 5;
public int MinIndependentEventsForDelete { get; set; } = 2;
public int MinDeleteObservationMinutes { get; set; } = 10;
```

`EnableAutoFriendDelete` defaults to `false` for phase one. The owner can enable it after local block and reporting behavior is verified.

## Error Handling

- If risk persistence cannot be read, start with empty risk state and log degraded diagnostics.
- If persistence cannot be written, do not delete friends; only log and report failure if owner reporting is available.
- If OneBot/NapCat delete action fails, keep local block active and report `delete_friend_failed`.
- If owner report fails, keep audit log with `owner_report_sent=false` so the next status command can reveal unreported actions.
- If protected-list parsing fails for a token, ignore that token and log a diagnostic; never treat malformed config as permission to delete.

## Testing Strategy

Use TDD for every phase.

Required unit tests:

- Risk event detector recognizes owner impersonation, prompt injection, private flood, suspicious file/link, unauthorized action requests.
- Risk score service applies weights, decay, thresholds, and persistence reloads.
- Blocklist policy ignores blocked users and never blocks owner.
- Auto delete policy blocks allowlisted/protected/owner/bot users.
- Cooldown and daily limits prevent repeated deletion.
- Owner notifier formats report with action, user id, score, threshold, reasons, and decision flags.

Required integration tests:

- Blocked private user message does not reach model dispatch.
- Blocked group user message does not reach model dispatch.
- User hitting 120 becomes locally blocked and owner is notified.
- User hitting 160 triggers friend delete only when auto delete is enabled and all protections pass.
- Delete failure leaves local block active and reports failure.

## Rollout

Phase 1: Local risk scoring and soft block only.

Phase 2: Owner reports and commands.

Phase 3: Auto-delete dry-run mode and audit.

Phase 4: Real friend delete gateway, default disabled, owner can enable.

This staged rollout keeps the bot useful immediately while reducing the risk of irreversible QQ friend operations.

## Open Decisions Fixed For Implementation

- Default automatic deletion threshold is `160`.
- Default local block threshold is `120`.
- Default real deletion is disabled until owner enables `EnableAutoFriendDelete`.
- Owner review is not required per delete once enabled, but owner report is mandatory after every automatic local block or deletion attempt.
- XiaYu is the only initial bot allowed to execute real friend deletion. MiXu can use local block but not real deletion until explicitly enabled later.
