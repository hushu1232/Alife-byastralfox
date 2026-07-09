# DataAgent V3.20 Shadow Replay Report

shadow_replay_report=true
replay_fixture_pack=true
source_fixture_pack=v3.19
shadow_only=true
default_result_changed=false
starts_runtime=false
stores_secrets=false
stores_sql=false
stores_hidden_context=false

V3.20 consolidates the V3.19 replay fixture pack into one offline shadow replay report. The report summarizes fixture-level shadow comparison categories and keeps the existing deterministic C# DataAgent result unchanged.

## Boundary

The report is offline and advisory. It does not start LangGraph, does not call a live sidecar, does not execute SQL, does not store secrets, does not store hidden context, and does not emit visible QChat text. C# remains responsible for validation, gating, execution, persistence, diagnostics, and user-visible output.

## Included Fixtures

- successful_advisory
- rejected_authority
- timeout_fallback
- invalid_schema
