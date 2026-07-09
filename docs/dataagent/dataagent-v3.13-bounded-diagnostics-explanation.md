# DataAgent V3.13 Bounded Diagnostics Explanation

V3.13 allows a sidecar to provide a bounded diagnostic explanation, but only as advisory text. C# remains responsible for validation, rejection, formatting, and any eventual diagnostics write.

## Safety Markers

bounded_explanation=true
advisory_only=true
csharp_write_authority=true
sidecar_write_authority=false
requests_visible_text=false
default_result_changed=false
unsafe_text_rejected=true
fallback_required=true

## Rejection Rules

The explanation is rejected when it is empty, over unsafe diagnostic boundaries, contains raw SQL, hidden context, tool route context, credentials, connection strings, QChat/QQ visible-output intent, or any text that asks to publish directly.

## Authority Boundary

The sidecar may explain why a shadow comparison or graph handshake behaved a certain way. It may not write diagnostics, write audit/progress/evidence, send visible QChat text, mutate checkpoints, decide routes, or execute SQL.
