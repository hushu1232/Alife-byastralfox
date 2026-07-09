# DataAgent V3.23 Manual Audit Bundle

manual_audit_bundle=true
bundle_writer=true
source_versions=v3.18-v3.22
manual_only=true
starts_runtime=false
installs_dependencies=false
default_result_changed=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false

V3.23 adds a manual audit bundle manifest for the existing V3.18 through V3.22 evidence chain. The bundle records tokenized references to the manual replay report artifact and artifact index, the replay id, comparison count, category counts, and the default-result boundary.

## Boundary

The bundle writer only summarizes already-built C# evidence records. It does not start LangGraph, does not install dependencies, does not call a sidecar, does not execute SQL, does not write secrets, does not store hidden context, and does not change the default DataAgent result.

## Evidence Chain

includes_smoke_result_artifact=true
includes_replay_fixture_pack=true
includes_shadow_replay_report=true
includes_manual_replay_report_artifact=true
includes_manual_artifact_index=true
