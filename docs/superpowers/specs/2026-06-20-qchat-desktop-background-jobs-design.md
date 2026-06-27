# QChat Desktop Background Jobs Design

## Goal

Make owner-approved QChat desktop business actions non-blocking for QQ chat while preserving the current safety boundary: owner only, XiaYu only, approved draft only, explicit whitelist only, no shell execution.

## Current State

`/qchat desktop draft execute <draft_id>` currently reaches `DesktopActionGateway.ExecuteAsync` from the QChat owner-command path and awaits the action result before replying. This is acceptable for `open notepad`, but it is not acceptable for future high-latency desktop steward work because the OneBot event queue processes normal QQ messages sequentially. A slow desktop action would delay later QQ messages.

The current real business action is intentionally narrow:

- Create a desktop action draft.
- Approve or reject the draft.
- Execute an approved draft.
- Only the exact action `open notepad` is supported.
- Execution uses `notepad.exe` with `UseShellExecute=false`.
- Shell execution, arbitrary executable paths, arguments, file mutation, window mutation, and process mutation remain unavailable.

## Design

Desktop execution will be split into a fast command path and a background job path.

The fast command path validates the request and returns immediately:

- The actor must be the configured owner.
- The active bot route must be XiaYu.
- The draft must exist.
- The draft must be approved.
- The draft must not already be executed.
- The requested action must be present in a desktop business action registry.

After validation, `/qchat desktop draft execute <draft_id>` enqueues a desktop business job and returns a short response:

```text
desktop_execution=queued job=<job_id> draft=<draft_id>
```

The background job path executes the approved draft outside the QQ event-processing path:

- Jobs are serialized through a single desktop business task queue.
- A running or queued job for the same draft is not enqueued twice.
- Job status is recorded as `Queued`, `Running`, `Succeeded`, or `Failed`.
- A draft is marked `Executed` only after the underlying business action succeeds.
- If execution fails, the draft remains `Approved`.

## Components

### DesktopBusinessActionRegistry

Owns the explicit business-action whitelist. It normalizes user-facing action text and resolves it to a registered action descriptor.

Initial registry contents:

```text
open notepad
```

No other action is allowed in this phase.

### WindowsDesktopBusinessExecutor

Executes a resolved whitelisted desktop business action. It no longer owns the whitelist inline; it delegates action resolution to the registry.

### DesktopBusinessTaskQueue

Accepts approved draft execution requests, records a job, returns immediately, and runs the real executor in the background. It provides recent-job and single-job read APIs for QChat status commands.

The queue records compact structured status. It does not write model prompts, chain-of-thought, full logs, or verbose runtime traces.

### QChat Desktop Commands

Add read-only status commands:

```text
/qchat desktop jobs recent
/qchat desktop job <job_id>
```

`/qchat desktop draft execute <draft_id>` changes from synchronous execution to queue submission.

## Token Policy

The task system must not become a token sink.

- Deterministic `/qchat desktop ...` commands are handled locally and are not dispatched to the model.
- Job logs are local structured records, not conversation context.
- Default QQ replies contain only compact status lines.
- Full job logs are not injected into long-term memory.
- A completion event may be summarized later, but only as a short outcome.
- Detailed diagnostics require an explicit owner command and must still be bounded.

## Error Handling

- Unsupported action returns `desktop_execution=denied reason=unsupported_action` and creates no job.
- Duplicate queued/running request returns the existing job id and does not call the executor again.
- Duplicate request after success returns `desktop_execution=denied reason=already_executed`.
- Executor failure records a failed job and leaves the draft approved.
- Draft status update failure records a failed job even if the underlying process action succeeded.

## Testing

Tests must prove the behavior at the boundary where regressions matter:

- Registry allows only `open notepad`.
- Unsupported action is denied before queueing.
- Execute returns queued quickly.
- A slow desktop job does not block later QQ owner commands.
- Successful job eventually marks the draft executed.
- Failed job leaves the draft approved.
- Duplicate queued/running execution does not call the executor twice.
- Job recent/detail commands return compact local status without model dispatch.
