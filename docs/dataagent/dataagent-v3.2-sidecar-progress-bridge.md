# DataAgent V3.2 Sidecar Progress Bridge

DataAgent V3.2 adds a sidecar progress bridge, not a production graph runtime.
The optional dev sidecar can return bounded progress-shaped data, but sidecar progress is untrusted input until C# validates and maps it.

## Authority Boundary

The bridge accepts sidecar-specific progress DTOs and maps safe events to
`DataAgentProgressEvent`. It publishes only through `IDataAgentProgressSink`,
so the existing `DataAgentProgressRecorder`,
`DataAgentProgressDiagnosticsPublisher`, and
`DataAgentProgressDiagnosticsFormatter` remain the recorder and owner
diagnostics authority.

Sidecar progress cannot prove SQL execution, cannot set `ExecutedSql=true`,
cannot mutate checkpoints, cannot write evidence, cannot decide Tool Broker
route state, cannot send QChat text, and cannot own QQ ingress.

## Validation

The bridge rejects progress with mismatched request or session ids, unknown
manifest nodes, undefined statuses, unsafe reason codes, unsafe messages,
unsafe fact keys, unsafe fact values, or over-budget event/fact payloads.
Accepted facts are bounded and stamped with safe C# facts such as:

```text
source=graph_sidecar
node=<manifest-node>
request_id=<handshake-request-id>
```

## Testing

default tests do not require Python. Default tests use fake sidecar progress and fake handshake responses. They do
not start Python, FastAPI, uvicorn, or a live sidecar, and they do not require
network access, QChat, QQ, PostgreSQL, browser automation, model calls, or a
live LangGraph runtime.

## Future Transport

SSE or NDJSON streaming can attach to the same bridge by parsing untrusted
sidecar progress DTOs and passing them to C#. That transport is outside V3.2.
The V3.2 outcome is the stable C# validation, mapping, and publication
boundary.
