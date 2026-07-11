# DataAgent V4.3 Cross-Module Advisory Value Score Design

**Date:** 2026-07-12
**Source baseline:** V4.2

## Goal

Measure whether a validated LangGraph advisory produces useful, safe cross-module planning value before V4.4 allows any production-shadow network call. V4.3 remains offline/manual and cannot execute, write state, route, or publish.

## Inputs

The C# evaluator consumes:

- one validated `DataAgentV42OperatorEvidencePacket`;
- up to six requested capability names from the existing V3.14 manifest registry;
- an explicit operator disposition: `Adopted`, `Useful`, `Rejected`, or `NotReviewed`;
- bounded review time before and after the advisory, supplied by the harness/operator.

LangGraph cannot set the disposition, review times, score, eligibility, or authority markers.

## Cross-module validation

Every requested capability must match one of the existing planner-only manifests:

- `qchat.intent_hint`
- `memory.candidate_summary`
- `browser.task_plan`
- `desktop.task_plan`
- `emotion.expression_hint`
- `deskpet.expression_hint`

The evaluator re-runs `DataAgentCrossModulePlannerManifestValidator` and requires `PlannerOnly=true`, `AllowsExecution=false`, `AllowsStateWrite=false`, and `AllowsVisibleText=false`. Unknown, duplicate, unsafe, or invalid manifests reject the evaluation. No new capability is accepted from advisory text.

## Deterministic score

The score is 0–100 and is computed only from authoritative facts:

```text
V4.2 packet accepted                         25
replay diff gate passed                     25
all requested manifests valid               20
operator disposition Adopted / Useful       20 / 10
bounded review-time reduction               0..10
```

Review times are integers from 0 through 3,600,000 milliseconds. The time component is proportional to reduction and is zero when the baseline is zero, the after value exceeds the baseline, or the disposition is `Rejected`/`NotReviewed`.

Result status is derived:

- `ProvenUseful`: score at least 80, replay aligned, and disposition is `Adopted` or `Useful`;
- `Promising`: score at least 60;
- `Unproven`: valid evaluation below 60 or not reviewed;
- `Rejected`: invalid input, unsafe content, rejected/fallback V4.2 packet, or invalid manifest.

`ProductionShadowEligible` is true only for `ProvenUseful`. This is evidence for V4.4 configuration; it does not enable a runtime by itself.

## Output and persistence

`DataAgentV43CrossModuleValueResult` contains only safe capability tokens, component scores, total score, derived status, operator disposition, production-shadow eligibility, stable reason codes, and false authority/write markers. A formatter and optional artifact writer emit the validated result without raw advisory text, prompt/context, private content, SQL, paths, or operator identity.

## Tests and readiness

Tests cover all four dispositions, exact score boundaries, duplicate/unknown capability rejection, invalid manifest rejection, rejected/fallback packet rejection, unsafe tokens, time bounds, time regression, formatter/artifact safety, and proof that no execution/write/visible-text authority is added.

Add dynamic/static readiness `GraphHandshakeV43CrossModuleValueScorePresent`, preserving V3 frozen counts. V4.3 default tests do not call a sidecar or require a live runtime.

## Non-goals

V4.3 does not call LangGraph, start Python, install dependencies, change DataAgent results, execute any manifest action, infer operator feedback, persist raw evaluation inputs, enable production shadow, or claim V4.5 closure.
