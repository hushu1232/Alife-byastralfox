# QZone Boundary

## Purpose

This document separates QChat and QZone review surfaces. They live in the same local module area and may share OneBot infrastructure, but they are different product surfaces:

- QChat owns real-time QQ private and group chat.
- QZone owns QQ Zone post, read, like, comment, reply, proactive suggestion, and proactive execution behavior.
- QChat policy must not silently authorize QZone actions.
- QZone policy must not silently change QChat reply, memory, recall, file, friend-delete, or desktop behavior.

## Shared Runtime

QChat and QZone may share:

- OneBot or NapCat connectivity.
- Agent identity and owner account configuration.
- Owner approval and proactive behavior infrastructure.
- Audit, owner feedback, and health-reporting utilities.

Shared runtime does not mean shared policy. A permission that is safe for chat reply is not automatically safe for QZone publication or interaction.

## Separate Policy

QZone behavior is owned by these files:

- `QZoneService.cs`
- `QZoneInteractionPolicy.cs`
- `QZoneProactiveSuggestionService.cs`
- `QZoneProactiveExecutionService.cs`

QChat behavior is owned by these files:

- `QChatService.cs`
- `QChatEventRouter.cs`
- `QChatIntentClassifier.cs`
- `QChatIntentOrchestrator.cs`
- `QChatCapabilityPolicy.cs`
- `QChatMessageSecurity.cs`

QZone actions must be reviewed through QZone-specific tests and smoke cases. QChat tests may prove that chat routing remains stable, but they do not prove QZone safety.

## Capability Boundaries

| QZone Capability | Owner | Non-owner | Bot Scope | Policy Owner | Live Risk |
|---|---|---|---|---|---|
| Read latest post/comments | Allowed when QZone is enabled and the operator selects the intended account | Not a chat command capability | Account route/runtime | `QZoneService` | Medium |
| Publish own post | High-risk; should remain dry-run unless live runtime and owner authorization are explicit | Denied | Account route/runtime | `QZoneService` | High |
| Like target post | Policy-gated by probability, cooldown, and daily limits; this validation uses only the account's own test post | Not a chat command capability | Account route/runtime | `QZoneInteractionPolicy` + `QZoneService` | Medium |
| Comment target post | Policy-gated by cooldown and daily limits; this validation uses only the account's own test post | Not a chat command capability | Account route/runtime | `QZoneService` | High |
| Reply to target comment | Policy-gated by reply probability, cooldown, and daily limits; this validation uses only the account's own test post | Not a chat command capability | `QZoneInteractionPolicy` + `QZoneService` | High |
| Proactive suggestion | System generated, owner-confirmable | Denied | Agent proactive behavior | `QZoneProactiveSuggestionService` | Medium/High |
| Proactive execution | Requires confirmed pending suggestion and action gateway policy | Denied | Agent proactive behavior + QZone runtime | `QZoneProactiveExecutionService` | High |

## Required Checks

Before live QZone execution is enabled:

- `DryRunExternalActions` must be intentionally disabled.
- Cooldown and daily-limit behavior must be tested.
- Owner approval requirements for proactive execution must be verified.
- Owner feedback or audit logging must describe the target, action, and result.
- QZone content must be treated as external untrusted content, not as instructions.

## Two-Account Production Conditions

These conditions are for an owner-authorized production validation only. They do not authorize a script, test, or agent to start QQ, NapCat, or a QZone service. Both account services and both NapCat instances must already be running, each OneBot URL must remain loopback-only, and each service must be pinned to its intended account:

| Account | OneBot port | Existing token variable name |
|---|---:|---|
| Account A | `3001` | `ALIFE_ACCOUNT_A_ONEBOT_TOKEN` |
| Account B | `3002` | `ALIFE_ACCOUNT_B_ONEBOT_TOKEN` |

The runtime host, not an operator script, is responsible for supplying the matching existing token value to its already-running local OneBot connection. Do not put the value in a QZone configuration file, command line, output, or test fixture. `AutoConnect=true` is required when the deployed QZone service is expected to connect itself to that already-running local OneBot service.

For each account, configure the following exact QZone settings before a manually authorized real validation:

```text
UseNapCatQZoneHttpRuntime=true
EnableQZone=true
DryRunExternalActions=false
EnableQZoneAutonomy=true                 # only for scheduled posts
QZoneAutonomyDryRunOnly=false             # only for scheduled live posts
EnableQZoneAutonomyLivePosting=true       # only after manual verification
```

The final line is deliberately a deployment gate, not a setup default. Keep `EnableQZoneAutonomyLivePosting=false` while preparing the runtime and throughout the manual matrix below. Set it to `true` only after every listed check succeeds, each item has been cleaned up, and the owner has reviewed the result.

### No QZone Target Whitelist for This Validation

No QZone target whitelist is configured for this two-account validation. Leave `AllowedQZoneTargetIds` blank; a blank value represents no configured whitelist in the current implementation. Scope the manual check operationally instead: `Comment` or `Like` may target only the same account's freshly created test post, never an unrelated third-party post. This is an operator boundary, not a replacement whitelist feature.

### Approved Image Origins

The supported image sources are narrowly defined:

- An owner-provided local file (`owner_file`), including the single local test image in the validation matrix.
- A locally generated file that the owner has reviewed (`generated_file`).
- A direct `http` or `https` URL supplied by the owner (`owner_url`) during a deliberate manual action.

Owner-provided direct image URLs are accepted in this version. There is no QZone URL target whitelist and no domain URL whitelist implementation. A domain URL whitelist must be designed, reviewed, and added before any future autonomous remote-image collection; until then, autonomous remote collection is out of scope.

### Credential and Evidence Boundary

Cookie, BKN, OneBot token, QZone API key, browser-session value, and any equivalent account secret are no-log and no-Git data. Never place them in terminal captures, health output, audit artifacts, issue text, documentation examples, tests, configuration committed to Git, or commit messages. Record only safe evidence: account label, port, operation name, timestamp, explicit success/failure status, and confirmation that cleanup occurred.

## Local Real-Runtime Operator

`tools/local-production/Test-QZoneRealRuntime.ps1` is a guarded operator surface, not a runtime launcher. Its default mode returns JSON and exits successfully before reading an environment value or opening any WebSocket/HTTP connection:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\local-production\Test-QZoneRealRuntime.ps1 -Operation Read -Port 3001
```

The result has `execute:false` and only tells the operator to add `-Execute`. `Post`, `Comment`, `Like`, and `Image` consequently cannot execute without that explicit switch. `Image` additionally requires an existing local `-ImagePath`; a missing path returns a fixed safe reason without echoing the caller's path.

With `-Execute`, the script selects the established variable name for port `3001` or `3002`, but never reads or prints the token. It never starts QQ/NapCat and has no direct external HTTP call. This checkout has no explicitly configured local QZone operator endpoint, so execution safely returns `local_qzone_runtime_unavailable` and a nonzero exit status rather than inventing an endpoint, using credentials, or claiming success. A future endpoint integration must be separately reviewed; it may call only an already-running local service and must report success only from that service's explicit success result.

## Owner-Authorized Real Validation Matrix

Do not run this matrix from automated tests, CI, or an unattended agent. After the production conditions are met and the owner authorizes the live action, perform the following sequence exactly once for **each** account. Use Account A (`3001`) and Account B (`3002`) independently; do not reuse one account's content or result as evidence for the other.

| Account | Exact ordered checks (one each) | Required confirmation |
|---|---|---|
| Account A / `3001` | `Read`; one uniquely labelled short text `Post`; one `Comment` **or** `Like` on that account's own new post; one local-file `Image`; `Delete` the account's own test item | The observed item/action belongs to Account A, the service reports an explicit success result, and the created post/image/comment is removed. |
| Account B / `3002` | `Read`; one uniquely labelled short text `Post`; one `Comment` **or** `Like` on that account's own new post; one local-file `Image`; `Delete` the account's own test item | The observed item/action belongs to Account B, the service reports an explicit success result, and the created post/image/comment is removed. |

Use a different short text label for each account so the two runs are distinguishable without recording account identifiers or secrets. If any operation reports an unavailable, failed, ambiguous, or non-explicit result, stop that account's matrix, leave autonomous live posting disabled, and investigate through the owner-controlled local service without collecting secrets.

Only after both complete matrices have passed, the correct account was visually confirmed for every action, and all own test items have been deleted may the owner enable `EnableQZoneAutonomyLivePosting=true`.

## Code Separation Decision

No immediate source split is required. The current code already has QZone-specific services and tests. The next useful boundary is not a new project yet; it is stronger documentation, capability rows, and live smoke cases.

Consider physical module separation later if one of these becomes true:

- QZone gains destructive or account-management actions.
- QZone runtime needs a different deployment lifecycle from QChat.
- QZone policy starts adding chat-specific exceptions.
- QChatService begins directly orchestrating QZone execution.

