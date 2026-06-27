# AstralFox Engineering Standards

## Purpose

This document defines the engineering rules for future AstralFox and Alife work.
It exists so future plans are not just feature lists, but executable engineering
contracts with clear component boundaries, event contracts, coding rules, safety
rules, and verification gates.

The project goal is not only to make a QQ bot respond. The goal is to build a
maintainable personal agent system that can grow without turning into hidden
state, fragile shortcuts, prompt-only behavior, or untestable live hacks.

## Core Principles

### 1. The Agent Is A System, Not A Prompt

Persona text can shape expression, but it must not be the only mechanism that
keeps behavior correct.

Use code-level services for:

- permissions;
- routing;
- file handling;
- deterministic QQ actions;
- task status;
- event recording;
- memory hygiene;
- visible reply filtering;
- live diagnostics.

Prompt text may explain intent to the model, but correctness must be enforced by
code where practical.

### 2. Human-Facing Output Is A Product Surface

Anything sent to QQ, UI, files, or logs visible to the owner must be treated as a
product surface.

QQ-visible text must not expose:

- internal route labels;
- diagnostic labels;
- permission tags;
- model/router implementation details;
- hidden thoughts;
- no-reply state labels;
- raw exception frames;
- managed-file internal ids unless the owner explicitly needs them;
- token, cookie, session, key, or credential material.

The bot may discuss AI and technical concepts as normal language, but it must not
declare itself to be an AI/bot/service/plugin when speaking in-character through
QQ.

### 3. Deterministic Actions Must Stay Deterministic

Actions such as sending QQ messages, uploading files, deleting messages, poking,
downloading files, approving tasks, writing files, or changing allowlists must not
fall back into casual model chat when they fail.

Required pattern:

1. Validate permissions and request shape before execution.
2. Execute through a deterministic runner or equivalent controlled action path.
3. Record diagnostics and task status.
4. Send dedicated owner-facing feedback only when there is a current safe reply
   session or an explicit notification target.
5. Never call `Poke(...)` or the model with raw deterministic failure text.

### 4. Owner Authority Is Explicit, Not Accidental

The owner can bypass low-information filters where the feature is owner-directed,
but owner authority must still be explicit in code.

Owner-only behavior must check:

- sender id;
- source session;
- command intent;
- risk level;
- confirmation state when required;
- target allowlist where applicable.

Do not infer owner intent from a message that merely looks technical. If the
message mutates files, sends cross-session QQ content, uploads/deletes files, or
changes permissions, route it through the owner control path.

### 5. Live QQ Behavior Must Feel Like A Person Using QQ

The QQ experience should feel like the character is using a QQ account, not that
QQ contains a bot runtime.

Design implications:

- replies should be complete, natural QQ messages;
- no internal status text in QQ;
- no artificial waiting without a purpose;
- waiting is allowed only for context settle, recall grace, debouncing, actual
  tool work, or rate/cooldown control;
- deterministic task progress should use human-readable feedback;
- model silence should become either no output or a short visible reply selected
  by visible reply policy, not an exposed state label;
- private and group outputs must not be mixed into one visible message.

### 6. Build Small Boundaries Before Adding Big Features

Every new feature must identify its component boundary before implementation.
If a feature cannot be tested without booting the full app or real QQ, split it
until at least the decision logic is locally testable.

Prefer:

- small services;
- clear records/options;
- injected runtime interfaces;
- focused tests;
- adapters around external systems;
- event records for diagnostics.

Avoid:

- giant service methods with hidden side effects;
- direct calls to external APIs from decision logic;
- stringly-typed state where records are available;
- prompt-only safety;
- broad refactors unrelated to the current plan.

## Component Standards

### Component Categories

Future code should fit into one of these categories.

| Category | Responsibility | Examples |
| --- | --- | --- |
| Runtime adapter | Talks to an external system. | OneBot, QZone, browser, filesystem, process runner. |
| Policy service | Decides whether something is allowed or should happen. | reply decision, file safety, permission gate. |
| Planner | Converts intent and state into an executable plan. | outbound planner, task planner. |
| Runner | Executes deterministic work and returns structured result. | QQ deterministic task runner, agent task runner. |
| Formatter | Converts structured state into visible text. | task feedback formatter, status formatter. |
| Store/cache | Owns persistence or cached runtime state. | memory storage, relation cache, managed files. |
| UI surface | Displays or edits state through existing UI patterns. | Blazor module config UI. |
| Diagnostics/event sink | Records what happened without controlling behavior. | QChat diagnostics, life events, audit log. |

Do not combine unrelated categories in one class unless it is an existing
boundary being carefully reduced.

### Required Component Shape

New components should define:

- a single primary responsibility;
- explicit constructor dependencies;
- input and output records for non-trivial calls;
- no direct global state access unless the existing platform API requires it;
- no direct QQ/file/process mutation inside a pure policy service;
- tests for success, denial, and failure paths;
- diagnostics for meaningful runtime decisions.

### Component Naming

Use names that describe responsibility, not implementation fashion.

Preferred:

- `QChatReplyDecisionPolicy`
- `QChatDeterministicTaskRunner`
- `AgentPermissionGate`
- `QChatTaskFeedbackFormatter`
- `QChatManagedFileService`

Avoid:

- `Helper`
- `Manager` without clear ownership;
- `Utils` for domain behavior;
- `NewService`;
- names that encode temporary implementation details.

### UI Component Rules

UI work must follow the existing Alife module UI style unless a plan explicitly
changes it.

Required:

- keep module UI inside existing Blazor/module configuration surfaces;
- use compact, operational layouts;
- expose state clearly before adding mutation controls;
- route mutation actions through existing permission and audit services;
- never let UI buttons bypass owner confirmation for high-risk actions;
- keep diagnostics and raw ids behind owner/admin surfaces;
- avoid nested cards and decorative dashboards for operational tools.

Future UI components must specify:

- owning service;
- read model;
- mutation actions, if any;
- risk level per action;
- audit event emitted per action;
- tests for service-level read/mutation behavior.

## Event Standards

### Event Types

Use structured events for runtime facts. Events are not prompts and not QQ
messages.

Required event families:

- `diagnostic`: internal runtime decisions and failures;
- `audit`: permissioned owner or agent actions;
- `life`: character-relevant external or self actions;
- `task`: deterministic or agent task progress;
- `approval`: pending, accepted, rejected, expired owner approvals;
- `memory`: memory write/read/sanitization events;
- `qq`: inbound/outbound QQ message, file, poke, recall, and QZone activity.

### Event Naming

Use kebab-case names with a stable subsystem prefix.

Examples:

- `qchat-send-failed`
- `qchat-recall-succeeded`
- `qchat-auto-poke-back-throttled`
- `agent-approval-created`
- `agent-task-failed`
- `memory-sanitized`
- `qzone-comment-skipped`

Do not rename event ids casually. They are contracts for logs, tests, and future
diagnostics.

### Event Payload Rules

Event payloads must be structured objects. Prefer stable fields over raw strings.

Common fields:

- `timestamp`;
- `source`;
- `actorUserId`;
- `senderRole`;
- `messageType`;
- `targetId`;
- `groupId`;
- `taskType`;
- `action`;
- `riskLevel`;
- `decision`;
- `succeeded`;
- `errorSummary`;
- `correlationId`;
- `sourceMessageIds`.

Never include:

- API keys;
- OneBot access tokens;
- cookies;
- raw private chat content unless explicitly needed for a local regression and
  redacted;
- full exception stacks in owner-visible messages;
- absolute paths in QQ-visible output unless the owner explicitly requested a
  file operation and the path is safe to reveal.

### Event Correlation

Any multi-step task should carry a correlation id or stable task id across:

- request received;
- policy decision;
- approval request;
- execution start;
- progress;
- success/failure;
- owner feedback.

If a future feature cannot explain "which input caused this output", add event
correlation before expanding it.

### Diagnostics Versus User Feedback

Diagnostics explain to maintainers what happened.
User feedback tells the owner what action state matters.

Do not send diagnostic wording directly to QQ.

Example:

- diagnostic: `qchat-one-shot-file-upload-failed`, error `NapCat upload failed`;
- owner feedback: `hello_world.c 没传成，目标是 925402131 群文件。NapCat upload failed`;
- non-owner visible output: usually no feedback or a safe minimal reply.

## Code Standards

### General C# Rules

Use existing project style:

- file-scoped namespaces where present;
- records for immutable data contracts;
- explicit async `Task` APIs;
- dependency injection through constructors where possible;
- small methods with domain names;
- `ArgumentNullException.ThrowIfNull` for required object inputs;
- `StringComparison.Ordinal` or `OrdinalIgnoreCase` for deterministic string
  comparisons;
- avoid culture-sensitive parsing unless the feature is explicitly culture-aware.

### Error Handling

Do not swallow errors silently unless the decision is intentionally best-effort
and diagnostic events are emitted.

For deterministic runtime work:

- catch at the runner boundary;
- return structured result;
- write diagnostics;
- send dedicated owner feedback only through a safe channel;
- do not call the model with raw failure text.

For policy failures:

- return denied/skipped result where possible;
- throw only for programmer errors, invalid direct API use, or legacy API
  contracts that already throw;
- tests must lock in whether a failure throws or returns a result.

### String Handling

Avoid ad hoc string parsing for structured inputs when a parser, record, or CQ
segment helper exists.

When string parsing is necessary:

- normalize line endings;
- trim only at defined boundaries;
- keep regexes named and tested;
- write tests for Chinese, English, CQ tags, ids, and punctuation variants;
- never parse security decisions from model-generated prose alone.

### Async And Concurrency

Every delayed task must have a purpose.

Allowed delay reasons:

- debounce;
- conversation settle window;
- recall grace period;
- rate limit;
- cooldown;
- actual external operation progress;
- retry with bounded policy.

Disallowed:

- random waiting only to feel human;
- unbounded background tasks;
- fire-and-forget work without diagnostics;
- timers that cannot be canceled or correlated.

Background tasks must:

- capture cancellation where practical;
- log failure diagnostics;
- avoid throwing onto thread pool unobserved;
- avoid mutating shared state without a lock/channel/owned scheduler.

### Filesystem Rules

File features must distinguish:

- observed incoming files;
- owner-approved downloads;
- managed local copies;
- generated output files;
- files selected for QQ upload;
- files scheduled for deletion.

Rules:

- do not auto-download arbitrary files without owner approval;
- store managed QQ files under a dedicated managed root;
- preserve original names but sanitize filesystem paths;
- keep metadata records separate from file content;
- owner can retrieve/delete managed files without redundant confirmation when the
  operation is already within owner-approved scope;
- non-owner file retrieval or forwarding requires owner confirmation;
- file deletion must be scoped to managed files or explicitly approved safe
  workspace paths.

### QQ Output Rules

All QQ output must pass through the existing visible output pipeline or an
equivalent sanitizer.

Rules:

- do not output internal state labels;
- do not combine private and group replies in one message;
- do not default to group `@` unless strong touch is required or owner asks;
- keep messages complete; avoid forced sentence splitting;
- use deterministic task feedback for file upload/download/delete/recall/poke
  status;
- if the model returns sectioned text such as "私聊主人:" and "群里回复:", select
  only the current session section;
- if the model returns no-reply state text, suppress or convert through visible
  reply policy.

## Deterministic Task Standards

### Runner Contract

A deterministic runner should accept:

- `TaskType`;
- optional file name;
- target type;
- target id;
- action delegate;
- optional progress callback in future extensions;
- optional feedback policy.

It should return:

- status;
- error summary;
- exception reference for diagnostics;
- original task context.

The runner must not call the model.

### Actions That Must Use Runner Or Equivalent

Required:

- QQ text/media send;
- cross-session QQ send;
- QQ file upload;
- QQ file download/read/delete;
- QQ recall;
- QQ poke;
- QZone post/comment/reply/like;
- owner approval execution;
- workspace file write/delete/move;
- shell/process execution;
- GitHub upload;
- long-running agent tasks.

### Feedback Rules

Owner-facing task feedback should be:

- short;
- concrete;
- target-aware;
- free of internal route labels;
- clear about success, failure, uncertain state, cancellation, or progress.

Failures should say what failed and what target was affected, but not leak raw
internal state.

Non-owner feedback should be minimized unless the owner has approved a public
response.

## Plan And Spec Standards

### Every Future Plan Must State

- goal;
- owner-facing behavior;
- affected components;
- event contracts;
- permission/risk model;
- deterministic task paths;
- data/storage changes;
- tests to add before code;
- live validation needed, if any;
- rollback or safe disable switch.

### Plan Format

Use this shape for new plans:

1. Objective
2. Current System Context
3. Scope
4. Non-Goals
5. Component Changes
6. Event And Diagnostics Changes
7. Permission And Safety Changes
8. Tests
9. Live Validation
10. Implementation Steps
11. Acceptance Criteria

### Acceptance Criteria

A task is not complete because code was edited.
It is complete when:

- local tests covering the new behavior pass;
- old adjacent tests still pass;
- build passes;
- runtime health check passes when live behavior is affected;
- diagnostics do not expose secrets;
- QQ-visible output does not expose internal labels;
- the owner can understand success/failure from visible feedback where feedback
  is expected.

## Testing Standards

### Test First For Behavior Changes

For bug fixes and behavior changes, write a failing test first.

The test should prove:

- old behavior fails in the expected way;
- new behavior passes;
- adjacent behavior is preserved.

### Test Levels

Use the smallest level that proves the behavior.

| Level | Use For |
| --- | --- |
| Pure unit test | formatters, policies, parsers, visible text filters. |
| Service test with fake runtime | QChat routing, deterministic actions, file handling. |
| Integration test | cross-service contracts, storage reload, UI read models. |
| Live test | OneBot/NapCat/QQ payload shape and real adapter behavior. |

Live tests do not replace local regression tests. If a live issue is found, add a
local test using the minimal redacted event shape.

### Fake Runtime Standards

Fake runtimes must support:

- success recording;
- injectable failures;
- delays for progress feedback;
- result ids where real APIs return ids;
- enough event raising to simulate inbound behavior.

Do not assert only that a mock was called. Assert user-visible state, runtime
records, diagnostics, or returned results.

## Permission And Safety Standards

### Risk Classes

Use these risk classes consistently:

- Low: read-only status, local formatting, safe diagnostics.
- Medium: QQ send, file upload to known target, recall own message, poke.
- High: file write/delete, shell/process execution, GitHub upload, allowlist
  change, cross-session public send, QZone write, non-owner requested file
  forwarding.

### Owner Confirmation

Owner commands may execute automatically only when:

- the actor is the owner;
- the target is known or explicitly provided;
- the action is within configured allowed scope;
- the action does not cross a high-risk boundary that already requires explicit
  confirmation;
- tests cover the bypass.

Non-owner commands that cause side effects require owner confirmation unless a
specific low-risk allowlist rule says otherwise.

### Whitelist Strategy

Prefer explicit allowlists for:

- group file sending;
- cross-session sends;
- QZone targets;
- external file retrieval;
- shell commands;
- workspace roots.

Owner commands can have privileged defaults, but the code should still record
that owner privilege was used.

## Memory And Persona Standards

### Persona Is Separate From Capability

Changing XiaYu or Miao persona must not reduce tool capability, file capability,
or agent ability.

Persona controls:

- tone;
- relationship wording;
- visible style;
- social distance;
- character-specific habits.

Capability controls:

- tools;
- permissions;
- runtime adapters;
- file access;
- event processing;
- memory;
- planning.

Do not mix these in one configuration field.

### Memory Hygiene

Memory must not store:

- raw secrets;
- private content unrelated to durable relationship/context;
- transient diagnostic noise;
- internal no-reply labels as character facts;
- failed model output as truth.

Memory writes should identify:

- source;
- confidence;
- scope;
- owner/private/group relevance;
- retention intent.

## Documentation Standards

### Documentation Types

Use the right document type:

- `docs/superpowers/specs`: design contracts and engineering standards.
- `docs/superpowers/plans`: executable implementation plans.
- `docs`: runbooks, live validation boundaries, account maps, upload workflows.
- source-adjacent comments: only for non-obvious implementation details.

### Documentation Must Be Operational

Avoid vague statements such as "improve stability" without defining:

- what breaks now;
- what component changes;
- what event proves it;
- what test proves it;
- what live check remains.

## Git And Upload Standards

The local worktree may be dirty. Do not revert unrelated user changes.

When uploading to GitHub through `D:\FOXD`, follow:

- `docs/github-upload-via-foxd.md`;
- tracked files only;
- no runtime data;
- no `Outputs`, `Runtime`, `Storage`, `Models`, `.codegraph`, `.worktrees`;
- temporary isolated worktree;
- push to `github/master` from the carrier repository.

If new files are required for upload, make sure they are tracked before using a
tracked-file snapshot workflow.

## Review Checklist For Future Work

Before starting implementation:

- Is there a spec or plan?
- Does the plan identify component boundaries?
- Does it define event names and payloads?
- Does it define permissions and owner behavior?
- Does it define deterministic runner paths?
- Does it list tests before code?
- Does it state live validation requirements?

Before claiming completion:

- Did the relevant tests pass freshly?
- Did the build pass freshly?
- Did live health pass if the client/runtime changed?
- Did grep confirm no internal status or old failure-injection strings remain?
- Did QQ-visible output stay human-facing?
- Did diagnostics avoid secrets?

## Default Future Improvement Flow

Use this flow for future AstralFox improvements:

1. Write or update a spec.
2. Write a concrete implementation plan.
3. Add failing tests for the first behavior.
4. Implement the smallest change that passes.
5. Run focused tests.
6. Repeat for the next behavior.
7. Run full adjacent test suite.
8. Build.
9. Restart and live-health-check when runtime code changed.
10. Upload only after verification and tracking required files.

## Non-Negotiable Rules

- No deterministic action failure may be routed into casual model chat.
- No owner-only mutation may be available to non-owner callers without explicit
  approval flow.
- No prompt-only safety for file, shell, QQ side-effect, QZone, or GitHub upload
  actions.
- No hidden thought, internal no-reply status, route tag, or diagnostic label in
  QQ-visible text.
- No unbounded background task without cancellation or diagnostics.
- No live-only fix without a local regression test when the issue can be
  represented locally.
- No broad cleanup that reverts unrelated user work.

## How To Use This Document

Future plans should cite this document and explicitly list any exception.

If a planned change conflicts with this standard, the plan must say:

- which rule is being relaxed;
- why the exception is necessary;
- what safety or test replaces the standard rule;
- how the exception will be removed later.

This keeps AstralFox engineering decisions explicit instead of accumulating
implicit one-off behavior.
