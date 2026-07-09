# DataAgent V3.12 Replay Parity Shadow Comparison

V3.12 adds a deterministic shadow comparison model for comparing the C# baseline outcome with a sidecar outcome. It is a diagnostic contract only. It does not let LangGraph replace the default result.

## Safety Markers

shadow_only=true
default_result_changed=false
replay_parity_required=true
default_enabled=false
default_tests_live_runtime=false
no_sql_authority=true
no_checkpoint_mutation=true
no_visible_text=true
fallback_required=true

## Comparison Categories

- match
- accepted_advisory_difference
- rejected_authority_claim
- fallback_used
- invalid_schema
- timeout_or_transport_failure

## Authority Boundary

The sidecar may be compared against deterministic C# output, but comparison cannot execute SQL, mutate checkpoints, decide tool routes, write diagnostics, publish QChat text, or change the default DataAgent result.

## V4 Gate

Default-chain advisory integration remains out of scope until shadow comparison and replay parity prove that sidecar output can be accepted, rejected, or ignored without changing the deterministic C# baseline.
