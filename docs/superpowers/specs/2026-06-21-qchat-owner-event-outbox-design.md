# QChat Owner Event Outbox Design

## Goal

Add a durable owner event outbox for QChat so high-importance owner reports and long asynchronous task results are not lost when NapCat, OneBot, or the local QChat process disconnects during delivery.

## Problem

QChat now sends several important owner-facing events through immediate OneBot private messages. This works while the QQ runtime is connected, but a long task can finish while NapCat is offline or while the service is restarting. In that case, the business action may have completed, but the owner may never receive the completion or safety report.

The most important current examples are:

- QChat risk reports, including local block and automatic friend deletion.
- Desktop business job completion notifications.
- Future high-risk real business actions such as file mutation, shell-like operations, desktop control, and account-affecting actions.

The owner event outbox must make event recording independent from QQ delivery.

## Non-Goals

- Do not persist every ordinary chat reply.
- Do not replay stale casual conversation after reconnect.
- Do not use the model to summarize or retry reports by default.
- Do not make non-owner users able to inspect, retry, or clear owner events.
- Do not replace existing desktop job logs, audit logs, or risk score logs.

## Recommended Architecture

Create a QChat-specific owner event outbox with two layers:

1. `QChatOwnerEventOutbox` persists owner events to a JSONL file and tracks delivery state.
2. `QChatOwnerEventDispatcher` attempts to deliver pending events to the owner through OneBot and marks successful deliveries.

The business flow becomes:

```text
important event produced
-> append or upsert owner event to local outbox
-> try to deliver pending owner events
-> if OneBot delivery succeeds, mark event delivered
-> if delivery fails, keep event pending with retry metadata
```

This means a disconnect affects only delivery timing. It does not erase the event.

## Storage

Use a JSONL file under the existing agent workspace:

```text
Storage/AgentWorkspace/qchat-owner-events.jsonl
```

Each write appends a full immutable event snapshot. Delivery updates are also appended as new snapshots for the same `event_id`. On load, the newest snapshot per `event_id` wins. This matches the existing append-only style used by desktop job and draft logs, avoids fragile in-place editing, and remains easy to inspect manually.

The outbox should retain a bounded recent history. The first implementation should keep at least 1,000 newest unique events or 7 days of delivered events, whichever is safer for a simple implementation. Pending events must not be trimmed unless they are older than a future explicit retention policy.

## Event Model

Use a record similar to:

```csharp
public sealed record QChatOwnerEventEntry(
    string EventId,
    DateTimeOffset CreatedAt,
    string AgentId,
    long OwnerId,
    string Severity,
    string Category,
    string Source,
    string SourceId,
    string DedupeKey,
    string Message,
    QChatOwnerEventStatus Status,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? DeliveredAt,
    long? DeliveryMessageId,
    string? LastError);
```

`Message` stores the machine-readable report body. Persona formatting should happen at dispatch time through the existing `QChatCommandPersonaFormatter`, so undelivered events benefit from future persona formatting fixes without losing the raw report.

Status values:

```text
Pending
Delivering
Delivered
Abandoned
```

`Delivering` is optional in the first implementation. If used, startup recovery should treat stale `Delivering` events as `Pending`.

## Dedupe Rules

Every producer must provide a stable `dedupe_key`. If an event with the same dedupe key already exists and is pending or delivered, the outbox must not create a duplicate owner notification.

Recommended keys:

```text
risk-local-block:{agentId}:{botId}:{userId}:{yyyyMMdd}
risk-delete-friend:{agentId}:{botId}:{userId}:{score}:{eventCount}
desktop-job:{jobId}:{status}
desktop-business-action:{draftId}:{jobId}:{status}
```

The first implementation can map `dedupe_key` to `event_id` by hashing or by sanitizing a short stable value. It must be deterministic across restarts.

## Delivery Policy

Only deliver to the configured owner private chat.

Delivery should use the existing OneBot runtime:

```text
SendPrivateMessageWithResult(ownerId, formattedMessage)
```

On success:

```text
status=Delivered
delivered_at=now
delivery_message_id=result.MessageId when available
last_error=null
```

On failure:

```text
status=Pending
attempt_count += 1
last_error=short sanitized error
next_attempt_at=now + retry_delay
```

Retry delay:

```text
attempt 1: 30 seconds
attempt 2: 2 minutes
attempt 3: 10 minutes
attempt 4+: 30 minutes
```

The dispatcher must not throw back into the business task path. A failed owner report must not make a completed business action look failed.

## Triggering Delivery

Flush pending events in these situations:

- Immediately after enqueueing a new owner event.
- When QChat starts and OneBot connects.
- Periodically from the QChat time-iterative loop if available.
- When the owner runs a retry command.

The dispatcher should use a small semaphore so overlapping flushes collapse into one active delivery loop.

## First Integration Points

### Risk Reports

Replace direct owner risk sends in `QChatService.SendOwnerRiskReportAsync` with:

```text
enqueue risk event
try flush owner outbox
```

This covers:

- `action=local_block`
- `action=delete_friend`, including success and failed gateway results

### Desktop Business Job Completion

Replace direct private sends in `QChatDesktopBusinessJobCompletionSink.NotifyCompletionAsync` with an outbox enqueue.

The completion sink must report only terminal states:

```text
Succeeded
Failed
```

The event should include:

```text
desktop_job={job.JobId}
status={job.Status}
draft={job.DraftId}
action={sanitized requested action}
```

### Future Real Business Actions

Future owner-only high-risk actions should publish owner events through the same interface instead of sending direct QQ messages.

## Owner Commands

Add owner-only QChat commands after the core outbox is working:

```text
/qchat events status
/qchat events recent
/qchat events retry
/qchat events detail <event_id>
/qchat events clear delivered
```

The first implementation only needs `status` and `retry` if keeping scope small is necessary.

Non-owner users must not be able to trigger these commands.

## Token Cost

This design should not materially increase token usage. Event persistence, retry calculation, and OneBot delivery are deterministic code paths. They do not call the model.

Token use only increases if a future feature asks the model to summarize event history. That is out of scope for the first implementation.

## Safety Properties

- Important owner events are recorded before delivery.
- QQ delivery failure does not erase the event.
- Reconnect or restart can retry pending events.
- Delivered events are deduplicated and not repeatedly sent.
- Ordinary chat messages are not replayed after reconnect.
- Only owner-facing high-importance events enter this outbox.
- Non-owner users cannot inspect, retry, clear, or trigger owner event delivery.

## Testing Requirements

Add focused tests for:

- Enqueue creates a pending event on disk.
- Recreating the outbox from the same file reloads pending events.
- Successful delivery marks the event delivered and stores message id when available.
- Delivery failure leaves the event pending with attempt count and next retry time.
- Duplicate dedupe key does not produce duplicate sends.
- Risk owner reports enqueue an owner event instead of being lost on send failure.
- Desktop job completion enqueues an owner event when delivery is unavailable.
- Owner command `events status` is owner-only.

## Rollout Plan

1. Implement the outbox and dispatcher with tests.
2. Wire risk reports to the outbox.
3. Wire desktop business job completion to the outbox.
4. Add minimal owner status and retry commands.
5. Run focused QChat tests and full solution tests.

## Open Decision

The first implementation should not outbox every current reply-session tool result. Only owner-important task results and safety reports should be persisted. This avoids replaying stale casual content and keeps the outbox focused on owner accountability.
