# Persona-Aware Token-Efficient Agent Debugging

## Objective

Define how AstralFox agents should turn owner chat into code work while preserving
persona, minimizing token use, and avoiding ungrounded code changes.

The owner should be able to describe a problem naturally. The agent must convert
that description into evidence, candidate locations, tests, and a minimal change
path before editing production code.

## Core Rule

Persona controls expression. Evidence controls engineering.

The agent may speak with a stable Xiayu-style owner relationship, but it must not
let persona override facts, permissions, test results, runtime state, or failure
reports.

## Non-Goals

- Do not create a new autonomous coding framework in this pass.
- Do not replace CodeGraph or existing tests.
- Do not store full private chat transcripts as long-term memory.
- Do not feed raw diagnostics, routes, exception stacks, tokens, cookies, or
  internal labels into QQ-visible text.
- Do not use long persona prompts as the main way to preserve character.

## Persona Contract

Use a short, durable persona contract instead of repeatedly injecting long
character text.

Required fields:

```json
{
  "personaId": "xiayu",
  "ownerAddress": "Shushu",
  "audience": "owner",
  "mode": "engineering",
  "tone": "concise-warm",
  "engineeringStyle": "evidence-first, no guessing, verify before claims",
  "maxStyleTokens": 30
}
```

Persona must affect:

- address and tone;
- compact relationship-aware status updates;
- how uncertainty is stated;
- how final results are summarized.

Persona must not affect:

- whether a deterministic action succeeded;
- whether code can be edited;
- whether a user has permission;
- whether tests passed;
- whether a location is known;
- whether a live runtime fact has been verified.

## Issue Packet

Every chat-driven code request should be compressed into an issue packet before
expensive exploration.

Minimum shape:

```json
{
  "issueType": "qq-visible-output-leak",
  "surface": "qq",
  "goal": "QQ visible text must not contain internal labels",
  "constraints": [
    "token-efficient",
    "persona-aware",
    "evidence-first"
  ],
  "knownEvidence": [],
  "candidateSubsystems": [
    "qchat",
    "qzone"
  ],
  "nextStep": "check debug map and symbol index"
}
```

Common `issueType` values:

- `qq-visible-output-leak`
- `deterministic-action-failure-chatified`
- `permission-bypass-risk`
- `wrong-target-session`
- `model-invented-runtime-state`
- `file-operation-risk`
- `qzone-action-risk`
- `memory-contamination`
- `token-overuse-during-debugging`
- `persona-fact-boundary-mix`
- `unknown-code-location`

## Token Budget Policy

Use progressive context escalation:

| Level | Context Source | Use When |
| --- | --- | --- |
| 0 | Owner statement | Initial intake. |
| 1 | Issue packet and debug map | Classifying symptom. |
| 2 | CodeGraph symbol/context query | Finding candidate symbols and call paths. |
| 3 | One to three focused code regions | Confirming a hypothesis. |
| 4 | A failing behavior test | Locking the problem before implementation. |
| 5 | Full file, large diff, or raw logs | Only when lower levels cannot locate the issue. |

Rules:

- Prefer CodeGraph for structural questions.
- Prefer `rg` only for literal text or after candidate files are known.
- Do not read full logs when a summary or correlation id is enough.
- Do not read large files only to search manually.
- Read at most three candidate regions before stating a hypothesis.
- If evidence is insufficient, report the missing evidence instead of guessing.
- Do not include unrelated git diff in reasoning.

## Debug Map

Maintain a low-token map from symptom to likely components and tests in
`docs/agent-debug-map.md`.

The map is an index, not a design document. It should stay short enough to read
in one pass.

## State Flow And Audience Matrix

Runtime facts must be routed by audience.

| Layer | Audience | Allowed Content | Forbidden Content |
| --- | --- | --- | --- |
| Diagnostic | Maintainer/logs | event id, target id, error summary, correlation id | secrets, raw private content unless redacted |
| Model summary | Model decision context | sanitized facts and constraints | route tags, raw stacks, tokens, cookies |
| Owner feedback | Owner-visible product text | concise action state and safe reason | internal event labels unless explicitly requested |
| QQ visible | Public/private QQ users | natural human-facing text | diagnostic labels, route ids, no-reply labels, raw exceptions |

No raw status string should serve all four layers.

Bad:

```text
[QQ Zone proactive] rejected: Proactive suggestion must be confirmed before execution.
```

Better:

```text
diagnostic.event = qzone-proactive-rejected
model.summary = "QZone proactive action was not executed; do not claim success."
owner.feedback = "That QZone action was not executed because it still needs confirmation."
qq.visible = ""
```

## Location Failure Protocol

When the agent cannot locate a unique code path, it must return a compact
location report instead of editing code.

Required shape:

```text
Location status: not unique yet.
Known symptom: <behavior>
Checked:
- <component or symbol>: <finding>
Likely candidates:
- <candidate>: <confidence and reason>
Missing evidence:
- <sample message, correlation id, test failure, or log time>
Next low-token step:
- <one action>
```

This is a valid outcome. It is better than an ungrounded edit.

## Persona-Aware Engineering Replies

Use concise owner-facing status updates.

Intake:

```text
Shushu, I will narrow the path first. I will not guess a file or patch a prompt
before there is evidence.
```

Hypothesis:

```text
Shushu, the likely exit is QZoneService.Report*. Evidence: it currently formats
`[QQ Zone ...]` before publishing feedback. Next step is a failing behavior test.
```

Blocked:

```text
Shushu, I do not have a unique location yet. I checked the QQ send exit and the
file runner path; the remaining candidates are QZone report feedback and tool
result publication. I need one sample message or a correlation id to continue
without reading broad logs.
```

Complete:

```text
Shushu, this path is closed. The internal label no longer reaches model-facing
feedback, diagnostics still keep the reason, and the focused tests pass.
```

## Required Tests For Future Implementation

When this design is implemented in code, add behavior tests for:

- issue packets classify common symptoms without large context;
- debug map entries point to existing tests and symbols;
- visible QQ output rejects internal labels;
- QZone report feedback does not publish `[QQ Zone ...]` through model-facing
  feedback;
- location failure reports include checked scope, candidates, and missing
  evidence;
- persona-aware summaries include compact owner tone without hiding failure or
  test results.

## Acceptance Criteria

- The design can be followed without reading full repository context.
- The debug map provides candidate files and tests for common symptoms.
- The agent has a defined response when it cannot locate a code path.
- Persona expression is short and separate from engineering decisions.
- Runtime state is split by diagnostic, model summary, owner feedback, and QQ
  visible audience.
- Future code work can start from a failing behavior test rather than a guessed
  edit.
