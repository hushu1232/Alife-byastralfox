# DataAgent Roadmap Reconciliation

This document is the current authority for the DataAgent roadmap after the V3
closure reconciliation. Historical plans remain useful design history, but do
not override this document or the repository-level upload rules.

## Current scope

```text
chatbi_console_required=false
chatbi_console_current_scope=false
chatbi_console_blocks_v3_closure=false
chatbi_console_committed_future_version=false
```

ChatBI Console is not part of the current Alife/DataAgent delivery. This
reconciliation does not add a Vue application, backend API, listener port,
authentication surface, or external service. A future console needs a separate
approved scope and must consume the existing C# DataAgent boundary rather than
replace it.

## V3 closure boundary

V3 closure freezes the canonical pre-freeze readiness identities at:

- 111 static identities;
- 95 core dynamic identities.

The current readiness surfaces remain larger by design: static readiness has
114 checks and dynamic readiness has 98 checks. The additional identities are
the V3.28 final-freeze gate and the separately scoped V4.0/V4.1 work; they are
not members of the V3 frozen identity set.

V3 closure does not start a runtime, install dependencies, call a sidecar,
grant sidecar authority, or change the default DataAgent result. V4 work remains
manual, explicitly scoped, and advisory until separately approved.

## Historical alignment

The NL2SQL documents dated 2026-06-27 describe early V1/V2/V3 intent. Their
references to a Vue or ChatBI console are historical deferred-work notes, not a
current commitment. Historical references to copy-based FOXD upload workflows
are likewise superseded by the current repository authority: this Alife
checkout is uploaded directly to `alife-byastralfox`, without copying source
into FOXD.

## Operator questions retained in current documentation

- 当前还有哪些 required gate 没通过？
- 哪些 readiness check 与 QChat、视觉或 TTS 有关？
- 哪些测试证明 runtime readiness 是 required？
- 最近一次测试通过、失败和跳过的数量是多少？
- 哪些文档与 DataAgent/NL2SQL 计划有关？
