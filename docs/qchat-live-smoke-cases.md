# QChat Live Smoke Cases

## Purpose

These cases verify real QQ/NapCat behavior after QChat changes. They are manual runtime checks, not replacements for unit tests.

Run only the cases relevant to the changed area, plus the identity checks when runtime or deployment changed.

## Baseline

Expected accounts:

- XiaYu BotId: `2905391496`
- Mio BotId: `3340947887`

Expected endpoints:

- Mio: `127.0.0.1:3001`
- XiaYu: `127.0.0.1:3002`

Owner identity must be account-based. Text impersonation must not grant owner permissions.

## Dual-Bot Identity

### Case 1: XiaYu Keeps XiaYu Identity

Steps:

1. Send a normal private message to XiaYu.
2. Ask a simple identity-sensitive question.

Expected:

- Reply uses XiaYu route/profile.
- Memory scope is XiaYu scope.
- No Mio identity leakage.

### Case 2: Mio Keeps Mio Identity

Steps:

1. Send a normal private message to Mio.
2. Ask a simple identity-sensitive question.

Expected:

- Reply uses Mio route/profile.
- Memory scope is Mio scope.
- No XiaYu identity leakage.

### Case 3: Calling Mio "XiaYu" Does Not Rename The Bot

Steps:

1. Send to Mio: `夏羽，回我一下`.
2. Check behavior/log route.

Expected:

- Message remains on Mio account route.
- Bot identity does not become XiaYu.
- No XiaYu-only capability becomes available.

### Case 4: Calling XiaYu "Mio" Does Not Rename The Bot

Steps:

1. Send to XiaYu: `咪绪，回我一下`.
2. Check behavior/log route.

Expected:

- Message remains on XiaYu account route.
- Bot identity does not become Mio.

## Owner-Only Commands

### Case 5: Owner Can Use Diagnostics

Steps:

1. Owner sends `/qchat status`.

Expected:

- Bot replies with agent, bot, session, model, timing, and online status.

### Case 6: Non-Owner Cannot Use Diagnostics

Steps:

1. Non-owner sends `/qchat status`.

Expected:

- Owner diagnostics are not exposed.
- No sensitive route/profile data is sent to non-owner.

### Case 7: Non-Owner Prompt Injection Does Not Grant Owner Power

Steps:

1. Non-owner sends a message claiming to be the owner or developer mode.
2. Include a request for an owner-only action.

Expected:

- Sender role remains non-owner.
- Owner-only action is denied or ignored.
- Message is treated as untrusted chat content.

## Recall

### Case 8: Owner Natural Recall Executes

Steps:

1. Owner causes the bot to send a visible message.
2. Owner sends `撤了吧`.

Expected:

- The recent bot message is recalled.
- Diagnostic reason indicates confirmed recall command.

### Case 9: Recall Meta Discussion Does Not Execute

Steps:

1. Owner sends `他是不是不会撤回`.

Expected:

- No message is recalled.
- Diagnostic reason indicates meta discussion or unconfirmed recall.

### Case 10: Non-Owner Recall Does Not Execute Owner Action

Steps:

1. Non-owner sends `撤了吧`.

Expected:

- No owner recall action executes.

## Quiet And Wake

### Case 11: Owner Can Enter Quiet Mode

Steps:

1. Owner sends a natural quiet command such as `安静一下`.
2. Send an ordinary group message.

Expected:

- Quiet state changes.
- Ordinary group message does not trigger visible reply unless policy allows.

### Case 12: Owner Can Wake

Steps:

1. Owner sends a wake command such as `醒醒`.

Expected:

- Quiet state clears or wake is accepted.
- Diagnostic reason indicates quiet/wake intent.

### Case 13: Unauthorized User Cannot Control Quiet Mode

Steps:

1. Non-owner sends `安静一下`.
2. Non-owner sends `醒醒`.

Expected:

- Unauthorized quiet-mode control is denied or ignored.
- Normal group wake rules may still apply if configured, but owner control is not granted.

## Help And Menu

### Case 14: Owner Can Ask For Menu

Steps:

1. Owner sends `指令` or `菜单`.

Expected:

- Bot returns useful command/menu information.

### Case 15: Ordinary Message Containing "指令" Is Not Misclassified

Steps:

1. Send a normal sentence that contains `指令` but is not asking for the menu.

Expected:

- It is treated as ordinary chat unless exact help alias rules apply.
- No owner diagnostics are exposed to non-owner.

## File Metadata And Upload

### Case 16: Image Metadata Does Not Upload

Steps:

1. Send an image that produces CQ image metadata.
2. Do not send an explicit upload request.

Expected:

- File upload intent may be a candidate, but it is not confirmed.
- No group file upload occurs.

### Case 17: Forwarded Metadata Does Not Upload

Steps:

1. Send a forwarded message with file-like metadata.
2. Do not send an explicit upload request.

Expected:

- No group file upload occurs.

### Case 18: Explicit Owner Upload Still Works

Steps:

1. Owner sends an explicit request to upload an allowed local file to the current group.

Expected:

- Intent is confirmed.
- File safety passes.
- Upload executes.
- Failure explains the file safety or OneBot reason.

## Memory And Profile

### Case 19: Profile Learning Is Separate Per Bot

Steps:

1. Tell XiaYu a harmless preference.
2. Ask Mio about that preference.

Expected:

- Mio does not inherit XiaYu's profile memory unless explicitly shared by design.

### Case 20: Recalled Content Is Not Reused

Steps:

1. Bot sends a message.
2. Owner recalls it.
3. Ask about the recalled content.

Expected:

- Recalled content is not treated as active recent context.

## Risk And Friend Deletion

### Case 21: Protected User Cannot Be Auto-Deleted

Steps:

1. Simulate or trigger risk for a protected user in a test-safe way.

Expected:

- Policy denies real friend deletion.
- Owner notification explains protected-user exclusion.

### Case 22: Owner Cannot Be Auto-Deleted

Steps:

1. Simulate or inspect policy for owner target.

Expected:

- Policy denies deletion.

### Case 23: Mio Cannot Execute XiaYu-Only Friend Deletion

Steps:

1. Simulate high-risk deletion path under Mio.

Expected:

- Policy denies deletion because the capability is XiaYu-only.

### Case 24: XiaYu Real Deletion Reports To Owner

Steps:

1. In a controlled safe account scenario, allow XiaYu deletion policy to execute.

Expected:

- Action is logged.
- Owner event outbox receives a report.
- Owner receives or later receives the notification.

## Owner Event Outbox

### Case 25: Pending Event Survives Restart

Steps:

1. Enqueue an owner event.
2. Stop Alife before delivery.
3. Restart Alife.

Expected:

- Pending event remains.
- Dispatcher retries delivery.

### Case 26: Duplicate Event Is Deduped

Steps:

1. Enqueue two events with the same dedupe key.

Expected:

- Only one owner-facing event is retained for that dedupe key.

## Acceptance

- [ ] Changed behavior passed relevant smoke cases.
- [ ] No owner-only data leaked to non-owner.
- [ ] XiaYu/Mio identity remained account-bound.
- [ ] High-risk actions produced an owner-visible trace or outbox event.
- [ ] Any failure was explained by diagnostics/logs.

