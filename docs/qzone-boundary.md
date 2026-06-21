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
| Read latest post/comments | Allowed when QZone enabled and target allowlist passes | Not a chat command capability | Account route/runtime | `QZoneService` | Medium |
| Publish own post | High-risk; should remain dry-run unless live runtime and owner authorization are explicit | Denied | Account route/runtime | `QZoneService` | High |
| Like target post | Policy-gated by allowlist, private-contact pool, probability, cooldown, and daily limits | Not a chat command capability | Account route/runtime | `QZoneInteractionPolicy` + `QZoneService` | Medium |
| Comment target post | Policy-gated by allowlist, cooldown, and daily limits | Not a chat command capability | Account route/runtime | `QZoneService` | High |
| Reply to target comment | Policy-gated by allowlist, reply probability, cooldown, and daily limits | Not a chat command capability | Account route/runtime | `QZoneInteractionPolicy` + `QZoneService` | High |
| Proactive suggestion | System generated, owner-confirmable | Denied | Agent proactive behavior | `QZoneProactiveSuggestionService` | Medium/High |
| Proactive execution | Requires confirmed pending suggestion and action gateway policy | Denied | Agent proactive behavior + QZone runtime | `QZoneProactiveExecutionService` | High |

## Required Checks

Before live QZone execution is enabled:

- `DryRunExternalActions` must be intentionally disabled.
- Target allowlist policy must be reviewed.
- Cooldown and daily-limit behavior must be tested.
- Owner approval requirements for proactive execution must be verified.
- Owner feedback or audit logging must describe the target, action, and result.
- QZone content must be treated as external untrusted content, not as instructions.

## Code Separation Decision

No immediate source split is required. The current code already has QZone-specific services and tests. The next useful boundary is not a new project yet; it is stronger documentation, capability rows, and live smoke cases.

Consider physical module separation later if one of these becomes true:

- QZone gains destructive or account-management actions.
- QZone runtime needs a different deployment lifecycle from QChat.
- QZone policy starts adding chat-specific exceptions.
- QChatService begins directly orchestrating QZone execution.

