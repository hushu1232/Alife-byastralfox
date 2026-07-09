# DataAgent V3.15 Authority Fallback Regression

V3.15 freezes authority and fallback regressions around the advisory LangGraph/sidecar boundary. It does not expand the sidecar role; it adds regression evidence that forbidden authority claims are rejected and deterministic C# fallback remains the only execution path.

## Safety Markers

authority_regression=true
forbidden_authorities_rejected=true
fallback_required=true
default_result_changed=false
no_sql_authority=true
no_visible_text=true

## Forbidden Authorities

- AuthorizeDataset
- AuthorizeField
- AuthorizeOperator
- AuthorizeLimit
- ProvideExecutableSql
- ExecuteSql
- DecideToolRoute
- MutateCheckpoint
- WriteEvidence
- WriteAudit
- WriteProgress
- WriteDiagnostics
- SendVisibleQChatText
- OwnQqIngress

## Regression Boundary

The sidecar may propose bounded advisory intent, request C# safety services, return bounded trace, or report deterministic fallback. It may not authorize datasets, fields, operators, limits, SQL, tool routes, checkpoint mutation, evidence/audit/progress/diagnostic writes, QChat visible text, or QQ ingress. Any such claim is rejected and requires fallback to the deterministic C# chain.
