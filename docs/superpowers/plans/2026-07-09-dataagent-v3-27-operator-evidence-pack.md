# DataAgent V3.27 Operator Evidence Pack Plan

## Goal

Create an operator-readable V3.27 evidence pack that aggregates the manual evidence chain from V3.18 through V3.26 without changing execution behavior.

Boundary:

- Agent suggests.
- Harness executes.
- C# validates.
- Artifact records.
- Readiness gates.
- Operator decides.

## Constraints

- Do not start LangGraph.
- Do not install dependencies.
- Do not call sidecar or network.
- Do not execute agent suggestions.
- Do not change default result.
- Do not write SQL, state, secrets, or hidden context.

## TDD Steps

1. Add `DataAgentV327OperatorEvidencePackTests` first.
2. Verify RED against missing V3.27 model/formatter/doc.
3. Add pure in-memory `DataAgentOperatorEvidencePack` builder and formatter.
4. Add V3.27 documentation markers.
5. Add dynamic and static readiness check `GraphHandshakeOperatorEvidencePackPresent`.
6. Update readiness count from 107 to 108 and core dynamic count from 92 to 93.

## Verification

Run targeted V3.27 tests, readiness-related tests, static readiness script, full DataAgent tests, `git diff --check`, stale-count search, and CodeGraph sync/status before commit.
