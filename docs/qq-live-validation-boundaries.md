# QQ Live Validation Boundaries

This document records the points where local tests are sufficient and where a real QQ/OneBot environment is required.

## QZone Bridge

Local tests currently verify:

- QZone external actions stay in dry-run mode unless explicitly configured for live mode.
- QZone post, comment, reply, and like calls are routed through `IQZoneRuntime`.
- `OneBotQZoneRuntime` uses configurable action names:
  - `PostAction`
  - `CommentAction`
  - `LikeAction`
- QZone target allowlists, private-contact like pool, probability checks, and confirmed proactive execution are enforced before runtime calls.

Real QQ/OneBot validation is required for:

- The exact plugin action names for QQ Zone posting, commenting, replying, and liking.
- The exact request field names expected by the installed OneBot/QZone adapter.
- Whether QQ Zone post ids and comment ids are exposed in the same format used by the local contracts.
- Whether the adapter reports success/failure synchronously or requires later event correlation.

No real QQ Zone write action should be enabled without owner confirmation, live-mode configuration, and an allowlisted target policy.

## QQ Chat Live Regression

The QQ chat pipeline has broad local coverage for deterministic behavior:

- Private and group message routing contracts.
- Sentence-level streaming buffering, so QQ does not receive unfinished sentence fragments.
- Group anti-spam decisions, cooldowns, and active soft attention windows.
- Quiet/sleep commands, trusted wake permissions, and owner-only control permissions.
- Suppression of internal status text such as no-reply decisions, quiet-state labels, and control policy notes.
- Relationship-scoped recent QQ experience context.
- Self-model guidance that keeps internal attention, fatigue, and desire labels out of user-facing chat.

Real QQ/OneBot validation is still required for end-to-end adapter behavior:

- Whether incoming private messages from the owner are observed and produce one model reply.
- Whether group `@` messages are observed and produce one model reply.
- Whether unmentioned group messages are usually suppressed in balanced mode.
- Whether image or sticker events arrive with enough CQ/event detail for occasional passive image replies.
- Whether the owner sleep command sends one persona-appropriate acknowledgement, then enters quiet mode.
- Whether mother QQ `3658431719` can wake quiet mode without receiving owner-level authority.
- Whether internal status text is absent from actual QQ messages.

Default live environment values:

| Field | Value |
| --- | --- |
| Bot QQ | `3340947887` |
| Owner QQ | `3045846738` |
| Mother QQ | `3658431719` |
| Test group | `867165927` |
| Private test target | `3425085583` |
| OneBot URL | `ws://127.0.0.1:3001` |

Live regression checklist:

| ID | Action | Expected result | Local coverage | Needs real QQ |
| --- | --- | --- | --- | --- |
| QQ-L1 | Owner sends a private text message. | Bot receives it and sends one complete model reply. | Routing and model-loop tests. | Yes, adapter event delivery. |
| QQ-L2 | Test group sends a message that mentions the bot. | Bot replies once, with no sentence fragments. | Mention routing and sentence buffering tests. | Yes, group event delivery. |
| QQ-L3 | Test group sends normal unmentioned chatter. | Bot usually stays silent in balanced mode and records the suppression reason internally. | Anti-spam probability and decision diagnostics tests. | Yes, real group timing/noise. |
| QQ-L4 | Test group sends an image or sticker without mentioning the bot. | Bot may occasionally reply if social attention allows it, but must not reply every time. | Passive image probability policy tests. | Yes, actual CQ image/sticker shape. |
| QQ-L5 | Owner tells the bot to sleep or be quiet. | Bot sends one persona-appropriate acknowledgement, then quiet mode suppresses normal group chatter. | Quiet command and no-status-leak tests. | Yes, final QQ text and state transition. |
| QQ-L6 | Mother QQ `3658431719` wakes the bot. | Bot wakes from quiet mode, but mother does not gain owner-only permissions. | Trusted wake and authority tests. | Yes, account identity mapping. |
| QQ-L7 | Group chatter continues after suppression or quiet mode. | Bot must not send internal texts like `(no reply)`, `keep quiet`, policy labels, or diagnostic reasons. | Internal output guard tests. | Yes, actual outgoing message audit. |

Result recording rules:

- Record only the test id, timestamp, observed message count, high-level result, and sanitized failure notes.
- Do not record OneBot access tokens, API keys, cookies, session tickets, or private message content that is not needed for debugging.
- If a failure depends on real QQ payload shape, save the minimal redacted event fields needed to add a local regression test.
