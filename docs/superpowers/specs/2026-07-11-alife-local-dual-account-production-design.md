# Alife Local Dual-Account Production Design

## Status and scope

This design starts after the completed DataAgent V3 closure work. It defines
the next productionization phase for one Windows machine only:

- two independently usable NapCat/QQ accounts;
- Alife workers that remain isolated by account;
- local supervision, health, recovery, and rolling restart;
- on-demand speech, vision, browser, and LangGraph capabilities;
- a C# local durable queue and resource leases.

This design does not add a public endpoint, cloud service, ChatBI Console, Vue
application, backend API, Redis, BullMQ, Node.js worker, FOXD source copy, or
default runtime startup.

## Goals

1. Both accounts can serve users simultaneously on the same local machine.
2. A failure or maintenance restart of one account does not stop the other.
3. Heavy capabilities start only when a user explicitly needs them.
4. Failed capability startup or execution degrades safely without installing
   dependencies, using the public network, or granting extra authority.
5. Status is visible locally and safe recovery summaries can reach the Owner.
6. Production readiness is proven through fault drills, not only launch tests.

## Non-goals

- Internet-facing management, queue, or health APIs.
- Cross-account QQ login, conversation, or task migration.
- Automatic model download, package installation, or browser acquisition.
- Default LangGraph startup or a C#-initiated sidecar authority path.
- Infinite concurrency or persistent always-on heavy models.
- BullMQ/Redis in the first productionization phase.

## Architecture

```text
Windows Task Scheduler
        |
        v
Local Supervisor
        |
        +-- Account A slot
        |     +-- NapCat A
        |     +-- OneBot A on loopback-only port A
        |     +-- Alife Worker A
        |
        +-- Account B slot
        |     +-- NapCat B
        |     +-- OneBot B on loopback-only port B
        |     +-- Alife Worker B
        |
        +-- Local durable queue and capability leases
              +-- speech adapter
              +-- vision adapter
              +-- browser adapter
              +-- LangGraph adapter
```

The Task Scheduler starts the local supervisor on boot or user logon. The
supervisor is the only lifecycle control plane. It has no public listener and
only uses loopback endpoints and the local file system.

Each account slot owns distinct configuration, runtime state, storage, ports,
logs, health history, reconnect backoff state, and secrets:

```text
config/account-a/       config/account-b/
runtime/account-a/      runtime/account-b/
storage/account-a/      storage/account-b/
logs/account-a/         logs/account-b/
```

Program binaries, read-only models and knowledge assets, and versioned
configuration templates may be shared read-only. QQ login state, OneBot token,
messages, session state, storage, and retry history must never be shared.

## Account health and recovery

Each slot is assessed at three layers:

| Layer | Healthy when | Recovery order |
|---|---|---|
| Process | NapCat and Alife worker remain alive | restart only the failed process |
| Connection | loopback OneBot endpoint is reachable and its session can reconnect | retry with backoff, then restart NapCat |
| Business | the worker can complete a safe local readiness/event-loop probe | drain, then restart worker or the slot |

Automatic reconnect is always preferred to restart. Each account records recent
failure time, continuous failure count, last restart, backoff deadline,
draining state, and an optional manual-maintenance lock. A restart threshold
prevents restart storms.

Rolling restart follows this state machine:

```text
healthy
  -> draining
  -> wait for active task completion or safe timeout
  -> restart only the target account component
  -> consecutive health probes pass
  -> healthy
```

While Account A drains or restarts, Account B continues to serve. A full local
status becomes `degraded` only when one account is unavailable; it becomes
`unavailable` only when both accounts are unavailable.

## Local durable queue and resource leases

The first phase uses a C# local durable queue backed by account-isolated local
state. BullMQ and Redis are explicitly deferred until a measured need for
multiple workers, distributed scheduling, or advanced delay/priority behavior.

Every heavy task persists only safe operational metadata:

```text
task_id
account_id
capability
created_at
deadline
attempt_count
state
safe_reason_code
```

Allowed task states are:

```text
queued -> starting -> ready -> running -> completed
queued|starting|ready|running -> timed_out|failed|cancelled|degraded
```

The machine has one lease per heavy capability by default:

| Capability | Default concurrency |
|---|---:|
| Speech | 1 |
| Vision | 1 |
| Browser | 1 |
| LangGraph | 1 |

Requests have bounded queues and deadlines. Running work is never preempted by
later work. Owner priority may be configured but cannot bypass safety deadlines.
When the supervisor restarts, explicitly retry-safe tasks may retry; tasks
whose idempotency cannot be established become `degraded` and require a new
user request.

## On-demand capability adapters

Every heavy capability implements the same local adapter contract:

```text
EnsureReadyAsync(deadline)
GetHealthAsync()
ExecuteAsync(request, cancellationToken)
DrainAsync()
StopIfIdleAsync()
GetSafeStatus()
```

The common flow is:

```text
explicit user request
  -> durable queue
  -> acquire capability lease
  -> EnsureReadyAsync
  -> health probe passes
  -> constrained execution
  -> C# validation and user response
  -> release lease
  -> idle-reclaim timer
```

| Adapter | Ready condition | Idle reclaim |
|---|---|---|
| Speech | model loaded and minimum synthesis probe succeeds | unload model or stop runtime |
| Vision | model or inference endpoint and minimum image probe succeed | stop runtime |
| Browser | isolated worker and loopback DevTools probe succeed | close context, then stop worker |
| LangGraph | manually authorized loopback health protocol succeeds | drain and stop sidecar |

All adapters are loopback-only. They must not download models, install
dependencies, write SQL, mutate checkpoints, write QQ visible text, or change
the default Alife result. The Alife/C# boundary remains the validation and
execution authority. Missing installation, startup timeout, unhealthy probes,
or unsafe adapter results produce a safe unavailable/busy/retry-later response.

## Observability and notifications

The supervisor exposes a local read-only status/report command rather than a
public management API. Its safe status includes:

```text
overall=healthy|degraded|unavailable
account-a/account-b process, connection, business, drain, queue and restart state
speech/vision/browser/langgraph stopped|starting|ready|draining|failed
```

The Owner may receive safe private summaries for account draining, recovery,
restart-threshold breaches, capability degradation, and both-account outage.
Messages must not include tokens, login state, chat content, full stack traces,
absolute paths, SQL, model input, or hidden context. If QQ itself is
unavailable, local logging and recovery continue without waiting for a notice.

## Production acceptance drills

Production readiness requires all of the following:

1. Both accounts independently process baseline local user requests.
2. Disconnecting Account A's OneBot link recovers A without affecting B.
3. Repeated Account A health failures cause A-only draining and restart while B
   continues serving.
4. A restart requested during active work drains or reaches a documented safe
   timeout; it does not directly kill a normal user request.
5. Simultaneous same-capability requests prove one lease, bounded queueing, and
   safe timeout degradation.
6. Each capability is exercised in unavailable, startup-timeout, and
   health-failure states, proving no public-network fallback, dependency
   installation, or authority expansion.
7. Restarting the supervisor proves durable task recovery and account isolation.
8. A continuous observation window shows no restart storm, state cross-talk, or
   unsafe alert leakage.

Only after every drill passes may the system be called **local dual-account
production ready**.

## Explicit future trigger for BullMQ

BullMQ may be reconsidered only after a documented need for multiple
independent workers, distributed queueing, complex delayed scheduling, or
capabilities that exceed the local durable queue's observed throughput and
operability. Introducing it requires a separately approved Redis lifecycle,
health, persistence, backup, and recovery design.
