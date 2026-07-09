# DataAgent V3.22 Manual Artifact Index

manual_artifact_index=true
manifest_writer=true
manual_only=true
starts_runtime=false
installs_dependencies=false
default_result_changed=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false

V3.22 adds a lightweight manifest for manually written shadow replay artifacts. The index records the artifact file token, replay id, comparison count, category counts, and default-result boundary for later manual audit.

## Boundary

The manifest writer only indexes an already-built C# report and artifact record. It does not parse hidden context, does not start LangGraph, does not install dependencies, does not call a sidecar, does not execute SQL, does not store secrets, and does not change the default DataAgent result.
