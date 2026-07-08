# DataAgent V3.3 NDJSON Streaming Transport

DataAgent V3.3 adds optional NDJSON streaming transport smoke coverage for the
graph handshake sidecar path. It is not a production LangGraph runtime, and the
C# application does not automatically start Python, FastAPI, uvicorn, or any
sidecar process.

## Stream Contract

The stream uses `application/x-ndjson`. Each line is one JSON object. Progress
events may appear before exactly one final response event:

```json
{"Kind":"Progress","Progress":{"NodeName":"query_planner","Status":"Completed","ReasonCode":"planner_suggested","Message":"planner ready","Facts":{"stage":"planner"}}}
{"Kind":"FinalResponse","Response":{"RequestId":"request-1","Accepted":true,"ReasonCode":"dev_sidecar_accepted","SelectedNodes":["scenario_knowledge","query_planner","diagnostics_router"],"NodeProgress":[],"TraceSummary":"ScenarioKnowledge:Completed>QueryPlanner:Completed>DiagnosticsRouter:Completed","ContextContribution":"graph_handshake_dev_sidecar=accepted","FallbackRequired":false,"NoSqlAuthority":true,"ReadOnly":true,"RequestedToolNames":["dataagent.query_plan.propose"],"RequestsCheckpointMutation":false,"RequestsVisibleText":false}}
```

Progress is untrusted sidecar input. C# buffers progress events until the final
response is received and accepted by `DataAgentGraphHandshakeValidator`. Only
after the final response is accepted may progress be published by
`DataAgentGraphSidecarProgressBridge`.

The dev stub final response may still include normal `NodeProgress` entries so
the non-stream `/handshake` endpoint keeps its V3.2 progress shape. In the
stream path, the coordinator publishes the buffered `Progress` envelope events
only after final response acceptance.

## Failure Semantics

The stream client reports explicit failure reason codes for stream transport
and schema failures:

- `invalid_stream_schema`
- `missing_stream_final_response`
- `stream_progress_over_budget`
- `sidecar_timeout`
- `sidecar_unavailable`

Rejected, invalid, timed out, unavailable, malformed, incomplete, and
over-budget streams publish no sidecar progress.

## Boundaries

The sidecar has no SQL authority, checkpoint authority, Tool Broker authority,
QChat authority, QQ authority, file authority, browser authority, diagnostics
authority, evidence authority, or plugin authority. It may suggest graph
handshake output only; C# remains the authority boundary.

The stream path does not grant database, file, browser, model, plugin, or
visible-chat behavior to the sidecar.

## Tests

Default tests use fake handlers and static checks. They do not require live
Python dependencies, FastAPI, uvicorn, a live port, network access, QChat, QQ,
PostgreSQL, browser automation, model calls, or a live sidecar.

## Future

SSE is deferred. V3.3 does not define `text/event-stream`, event ids,
heartbeats, reconnect semantics, or browser-facing streaming behavior.
