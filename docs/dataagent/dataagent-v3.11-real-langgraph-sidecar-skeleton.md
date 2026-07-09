# DataAgent V3.11 Real LangGraph Sidecar Skeleton

V3.11 introduces a manually runnable local sidecar skeleton. It is not started by Alife, not required by default tests, and not allowed to execute SQL or mutate state.

## Safety Markers

manual_only=true
loopback_only=true
default_enabled=false
default_tests_live_runtime=false
starts_runtime=false
installs_dependencies=false
creates_venv=false
binds_port=false
supervises_process=false
no_sql_authority=true
no_checkpoint_mutation=true
no_visible_text=true
no_tool_route_authority=true
fallback_required=true

## Manual Environment

Set these only when intentionally smoke-testing the sidecar:

ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENABLED=true
ALIFE_DATAAGENT_GRAPH_HANDSHAKE_ENDPOINT=http://127.0.0.1:8765/handshake
ALIFE_DATAAGENT_GRAPH_HANDSHAKE_TIMEOUT_MS=800

## Authority Boundary

The sidecar may propose orchestration intent, request C# safety service, return bounded trace, and report deterministic fallback. C# remains the authority for validation, execution, writes, diagnostics, audit, progress, and visible output.
