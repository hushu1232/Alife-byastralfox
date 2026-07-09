# DataAgent V3.21 Manual Replay Report Artifact

manual_replay_report_artifact=true
artifact_writer=true
manual_only=true
starts_runtime=false
installs_dependencies=false
default_result_changed=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false

V3.21 adds a local artifact writer for the V3.20 shadow replay report. Operators can write the offline markdown report to a chosen file path after manual replay or smoke review.

## Boundary

The writer only serializes an already-built C# report. It does not start LangGraph, does not install dependencies, does not call a sidecar, does not execute SQL, does not store secrets, does not store hidden context, and does not change the default DataAgent result.
