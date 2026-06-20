# Desktop Steward Safety Model

## Purpose

Desktop steward features let Alife inspect local computer state through explicit,
owner-controlled capabilities. The current implementation is intentionally
read-only and is enabled only for the XiaYu QChat bot identity.

## Current Scope

Enabled commands:

```text
/qchat desktop status
/qchat desktop health
/qchat desktop processes
/qchat desktop windows
/qchat desktop capabilities
```

Current hard limits:

- Only the owner account can trigger desktop diagnostics.
- Only the `xiayu` agent can trigger desktop diagnostics.
- MiXu and unknown bot identities are denied even when the message sender is
  the owner.
- Non-owner denial responses do not include process names, window titles,
  paths, stack traces, or diagnostic internals.
- Commands are deterministic and do not dispatch to the model.
- The feature is read-only. It does not execute shell commands, close
  processes, modify files, install software, edit registry state, or send
  network data.
- The capability list is informational. Listing a capability does not grant
  permission to mutate the computer or bypass the owner and XiaYu gates.

## Identity Rules

Owner authority is account-based. A message claiming to be the owner is not
trusted unless its sender account id matches the configured owner id.

Bot eligibility is also account/route based. The current allowlist is:

```text
agent=xiayu
bot=2905391496
```

The MiXu account is intentionally excluded:

```text
agent=mixu
bot=3340947887
```

## Capability Inventory

The owner can ask XiaYu for the active read-only desktop capability inventory:

```text
/qchat desktop capabilities
```

The response must include:

```text
desktop_mutation=disabled
shell_execution=disabled
```

Those lines are part of the safety contract. If either value changes, the
change must be covered by tests, approval policy, audit logging, and an updated
implementation plan.

## Audit Log

Desktop gateway attempts are persisted to:

```text
Storage/AgentWorkspace/desktop-action-audit.jsonl
```

The audit log records action metadata only:

- timestamp
- action name
- actor user id
- agent id
- risk
- success flag
- sanitized message

The audit log must not persist process lists, window titles, file contents,
credential material, stack traces, cookies, tokens, or arbitrary paths. Messages
are normalized to a single line and capped at 200 characters.

## Risk Rules

Desktop capabilities must be classified before they are exposed:

| Risk | Meaning | Current policy |
| --- | --- | --- |
| ReadOnly | Reads low-sensitivity runtime state | Allowed for owner + XiaYu |
| Low | Opens or navigates without mutation | Not implemented |
| Medium | Reversible local mutation | Not implemented |
| High | Destructive, execution-bearing, or privacy-sensitive | Not implemented |
| Critical | System-wide or credential-sensitive | Disabled |

## Action Gateway

Desktop action routing now has a gateway shell. It performs:

1. Capability lookup.
2. Risk classification.
3. Owner and bot eligibility checks.
4. Unknown action denial.
5. Mutation denial while `desktop_mutation=disabled`.
6. Execution only through a named handler.
7. Audit sink recording for attempts and outcomes.

Current gateway limits:

- Current QChat desktop diagnostics route through the gateway as registered
  `ReadOnly` actions.
- Only registered `ReadOnly` actions can execute.
- `Low`, `Medium`, `High`, and `Critical` actions are denied while desktop
  mutation is disabled.
- Approval flow is not connected yet.
- No real mutating desktop action is registered.
- Recovery or rollback guidance is not implemented yet because no mutation is
  implemented yet.

Arbitrary shell execution must not be exposed as a generic desktop capability.
