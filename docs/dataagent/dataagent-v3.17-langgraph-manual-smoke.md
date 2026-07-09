# DataAgent V3.17 LangGraph Manual Smoke

manual_smoke=true
operator_only=true
default_result_changed=false
sidecar_write_authority=false
csharp_execution_authority=true
fallback_required=true
manual_only=true
loopback_only=true

V3.17 provides an operator-only smoke harness for an already-running LangGraph sidecar. It does not start Python, install dependencies, create a virtual environment, bind a port, or make LangGraph part of the default DataAgent chain.

The smoke validates only advisory output. C# remains responsible for graph handshake validation, SQL authority, tool routing, checkpoint writes, evidence/audit/progress/diagnostic writes, visible text, and fallback.

## Operator Flow

Start the sidecar manually from `tools/dataagent-langgraph-sidecar` using a loopback host. Run `tools/run-dataagent-langgraph-manual-smoke.ps1` against the `/handshake` endpoint. Stop the sidecar from the same operator terminal after the smoke completes.

## Boundary

The manual smoke harness can prove transport reachability and advisory response shape. It does not promote LangGraph to execution authority, does not write diagnostics or checkpoints, and does not publish visible QChat text.
