# QChat Layered Contract Enhancement Design

## Goal

Strengthen QChat's Harness, Loop, and Prompt engineering map with tests that express the contracts behind the architecture.

The enhancement is layered:

- Required baseline contracts cover capabilities that exist in a clean checkout of the committed baseline.
- Optional active-workspace contracts cover capabilities present in this working tree but not guaranteed by a clean checkout, including TTS warmup, semantic windows, XiaYu private state prompts, and persona intensity/frame prompt extensions.

This split prevents the static checker and documentation from treating uncommitted local capabilities as required committed baseline behavior.

## Scope

This work adds or extends tests and updates the engineering map/checker to surface those tests.

Prefer test-only changes. Production code may be changed only when a test exposes a small missing contract boundary that cannot be tested or satisfied otherwise.

The work does not start live services, send QQ messages, call external models, read API keys, or require NapCat/GPT-SoVITS/Agnes to be running.

## Contract Areas

### Prompt Leak Contract

Purpose: internal prompt/state/context blocks must be allowed in model input but must not leak to human-visible text surfaces.

Required baseline contracts:

- Add or extend baseline prompt leak tests around visible reply/text policy behavior.
- Verify internal block markers are treated as invisible or sanitized before becoming human-facing output.
- Cover baseline markers and policies that exist in a clean checkout.

Optional active-workspace contracts:

- If `QChatSemanticWindowSummary` exists, verify `[semantic_window]` and `[untrusted_image_analysis]` blocks are model-context blocks, not visible reply text.
- If `XiaYuStatePromptFormatter` exists, verify `[XiaYu state - private, do not quote]` blocks cannot leak through visible reply/text selection.
- If persona frame prompt support exists, verify `[qchat persona frame]` is treated as internal context.

Acceptance criteria:

- Internal prompt blocks can be asserted in model-bound formatted input where appropriate.
- Internal prompt blocks are rejected, stripped, or neutralized before QQ-visible text.
- Internal prompt blocks are not treated as TTS-visible utterances.
- Tests are deterministic and do not call external services.

### Loop Invariant Contract

Purpose: QChat's runtime loops should have explicit safety invariants, not just runnable code.

Required baseline contracts:

- Extend `QChatContinuationPolicyTests` so deterministic task feedback continuation is policy-controlled.
- Verify low-value or non-actionable feedback does not create an unbounded continuation path.
- Verify sender/owner safety context is not bypassed by continuation decisions.
- Extend owner event/outbox tests only if the committed baseline exposes a stable outbox dispatcher contract.

Optional active-workspace contracts:

- Extend `QChatSemanticSettleWindowTests` when semantic settle support exists.
- Verify bursts settle as a single window.
- Verify recalled messages are removed before snapshot.
- Verify incomplete trailing text waits until the settle delay or maximum window boundary.
- Verify max-message or max-window pressure forces a bounded dispatch instead of indefinite waiting.

Acceptance criteria:

- Continuation decisions are deterministic and bounded.
- Semantic settle behavior is bounded and does not duplicate retained messages.
- Retry/outbox behavior does not block the main QChat message loop.
- Optional loop tests can be absent from a clean checkout without making the static checker fail.

### TTS Warmup Contract

Purpose: bot startup should be able to start TTS warmup in the background without blocking the main runtime, while keeping profile readiness truthful.

Required baseline contracts:

- Do not require `QChatVoiceWarmupCoordinator` in the committed baseline if the file is not committed.
- Keep the warmup checker entries optional until the warmup implementation is part of the baseline.

Optional active-workspace contracts:

- Extend `QChatVoiceWarmupCoordinatorTests` if the coordinator exists.
- Verify `StartAsync` returns before warmup completes.
- Verify unreachable endpoints do not call synthesis and do not report `Ready`.
- Verify a later reachable probe can transition the profile to `Ready`.
- Verify XiaYu and Mixu profile statuses are independent.
- Verify cancellation stops retry scheduling.

Acceptance criteria:

- TTS warmup does not block bot startup.
- Unreachable endpoints remain truthful.
- Ready state is reached only after a successful probe/synthesis path.
- Multiple voice profiles are tracked independently.
- Retry behavior is bounded and cancellation-aware.

## Static Checker Updates

Update `tools/check-qchat-engineering-map.ps1` without removing the existing required/optional distinction.

Required checker anchors should include only tests and source markers that exist in a clean checkout.

Optional checker anchors should include active-workspace tests for:

- Prompt leak optional contracts.
- Semantic settle invariant contracts.
- Voice warmup contracts.
- XiaYu state prompt leak contracts.

The checker must continue to:

- Use only static file and marker checks.
- Avoid service startup.
- Avoid network calls.
- Avoid API key reads.
- Avoid runtime writes.
- Exit nonzero only when required baseline anchors are missing.

## Documentation Updates

Update `docs/qchat-harness-loop-prompt-engineering.md` to describe the new test contracts.

The document should make clear:

- Required tests are committed-baseline contracts.
- Optional tests are active-workspace contracts.
- Optional missing entries are informative and do not imply baseline failure.
- Static checks are not equivalent to full build/test/live service validation.

## Verification

Run the static checker:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-engineering-map.ps1
```

Run the focused QChat tests when dependency restore/build is available:

```powershell
dotnet test Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter "FullyQualifiedName~QChatPromptLeakContractTests|FullyQualifiedName~QChatContinuationPolicyTests|FullyQualifiedName~QChatSemanticSettleWindowTests|FullyQualifiedName~QChatVoiceWarmupCoordinatorTests"
```

If NuGet repository-signature access still fails with `NU1301`, report that build/test verification is blocked by package-source policy and do not claim the tests passed.

Also verify the required/optional split in a clean temporary worktree at `HEAD`:

- Required anchors must pass.
- Optional anchors may be present or missing.
- Optional missing anchors must not cause a nonzero exit code.

## Non-Goals

- Do not start NapCat.
- Do not start GPT-SoVITS.
- Do not send live QQ messages.
- Do not call Agnes vision.
- Do not rewrite QChatService architecture.
- Do not convert optional active-workspace features into required baseline by checker wording alone.

## Risks

- The working tree contains many pre-existing changes. Implementation must avoid reverting or staging unrelated files.
- Some optional feature files may be untracked or partially staged. Tests for them must be treated as optional until those capabilities are deliberately committed into the baseline.
- Full `dotnet test` may remain blocked by NuGet `NU1301`; static checker success must not be overstated as full test success.

## Future Work

After this layered contract enhancement, the next increments are:

- Promote optional contracts to required when their production features are committed.
- Add prompt snapshot/golden-file tests for long-lived internal prompt blocks.
- Add runtime metrics for queue depth, settle pending count, TTS warmup attempts, and continuation decisions.
- Add a consolidated QChat runtime harness report that combines static anchors, focused tests, and live readiness checks.
