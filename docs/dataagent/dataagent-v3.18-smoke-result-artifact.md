# DataAgent V3.18 Smoke Result Artifact

smoke_result_artifact=true
artifact_formatter=true
manual_only=true
stores_secrets=false
stores_sql=false
stores_hidden_context=false
sanitizes_unsafe_text=true
default_result_changed=false

V3.18 adds a local formatter for results captured from the manual LangGraph smoke harness. It formats an already-captured JSON response into a small operator artifact while redacting unsafe trace and context text.

The artifact is manual-only. It does not call a live sidecar, start Python, install dependencies, create a virtual environment, bind ports, write checkpoints, publish visible text, or change the deterministic C# DataAgent result.

## Artifact Boundary

The formatter stores only bounded metadata such as request id, reason code, booleans for authority and fallback, and redacted trace/context summaries. SQL-looking text, hidden-context markers, bearer/token/key/secret strings, and visible QChat text markers are replaced with `redacted`.
