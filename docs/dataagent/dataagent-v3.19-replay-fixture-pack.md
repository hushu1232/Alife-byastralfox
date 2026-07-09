# DataAgent V3.19 Replay Fixture Pack

replay_fixture_pack=true
successful_advisory=true
rejected_authority=true
timeout_fallback=true
invalid_schema=true
default_result_changed=false
stores_secrets=false
stores_sql=false

V3.19 adds a deterministic replay fixture pack for the manual LangGraph smoke path. The fixtures cover successful advisory output, rejected authority claims, timeout fallback, and invalid schema fallback.

## Boundary

The fixtures are static replay inputs for tests and readiness. They do not call a live sidecar, do not start a runtime, do not store SQL, do not store secrets, do not store hidden context, and do not change the default C# DataAgent result.

## Fixtures

- successful_advisory
- rejected_authority
- timeout_fallback
- invalid_schema
