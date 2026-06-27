# QChat Harness, Loop, and Prompt Engineering Map Design

## Goal

Make the existing QChat Harness, Loop, and Prompt engineering practices explicit and repeatably checkable.

The project already contains the relevant runtime loops, prompt formatters, safety policies, tests, and readiness scripts. This work turns that implicit structure into:

- A maintainable engineering map document.
- A static preflight script that reports whether the key engineering anchors still exist.

## Scope

This change adds documentation and a static inspection tool only.

It does not change QChat runtime behavior, TTS startup behavior, vision routing, OneBot connectivity, state-machine logic, prompt text generation, or model calls.

## Outputs

### Engineering Map Document

Add `docs/qchat-harness-loop-prompt-engineering.md`.

The document will define and map three engineering layers:

- Harness engineering: tests, fake runtimes, readiness checks, startup scripts, live validation entry points.
- Loop engineering: OneBot receive loop, QChat event queue, tick/update loop, semantic settle window, continuation gate, XiaYu state machine, TTS warmup/retry, owner outbox dispatch.
- Prompt engineering: stable persona prompt, persona frame prompt, conversation cognition prompt, XiaYu private state prompt, semantic window summary, image analysis prompt, untrusted external context, context budget composition, visible output sanitization.

For each layer, the document will include:

- What the layer means in this project.
- Key source files and test files.
- Runtime role in the QChat pipeline.
- Current maturity assessment.
- Known gaps and recommended next steps.

### Static Engineering Map Check

Add `tools/check-qchat-engineering-map.ps1`.

The script will perform static checks only:

- Verify required files exist.
- Verify required symbols or marker strings exist in those files.
- Print grouped results for Harness, Loop, and Prompt engineering.
- Exit with code `0` when all required anchors are present.
- Exit with code `1` when any required anchor is missing.

The script will not:

- Start Alife Client.
- Start NapCat.
- Start GPT-SoVITS.
- Call OneBot.
- Call Agnes vision.
- Read API keys.
- Call external networks.
- Modify runtime state.

## Proposed Check Anchors

### Harness

Representative anchors:

- `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`
- `Tests/Alife.Test.QChat/QChatVisionReadinessTests.cs`
- `Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs`
- `Tests/Alife.Test.QChat/QChatModelReplyLoopLiveTests.cs`
- `tools/check-qchat-runtime-readiness.ps1`

These show the project has test harnesses, fake/runtime adapters, live validation boundaries, and operational readiness checks.

### Loop

Representative anchors:

- `OneBotClient.ReceiveLoop`
- `QChatService.ProcessOneBotEventQueueAsync`
- `QChatService.ITimeIterative.OnUpdate`
- `QChatService.ScheduleSettledDispatch`
- `QChatService.DispatchSettledConversationAsync`
- `QChatService.EnableContinuationGate`
- `QChatVoiceWarmupCoordinator`
- `QChatContinuationPolicy`
- `XiaYuSelfStateMachine.Apply`
- `QChatOwnerEventDispatcher.FlushAsync`

These show the project has message receive loops, queue consumption, runtime update loops, semantic debounce/settle loops, model continuation decisions, TTS retry/warmup loops, state feedback loops, and owner-event dispatch loops.

### Prompt

Representative anchors:

- `QChatService.RegisterStablePersonaPromptIfNeeded`
- `QChatPersonaIntensityPromptFormatter`
- `QChatService.FormatPersonaFramePrompt`
- `QChatConversationCognition.BuildInternalPrompt`
- `QChatService.BuildAddressPrompt`
- `QChatService.BuildQuietModeAcknowledgementPrompt`
- `XiaYuStatePromptFormatter`
- `[XiaYu state - private, do not quote]`
- `QChatSemanticWindowSummary`
- `[semantic_window]`
- `ExternalContextFormatter.WrapUntrusted`
- `ContextBudgetComposer`
- `QChatVisibleTextPolicy`
- `QChatVisibleReplyPolicy`
- `QChatExperienceSanitizer`

These show the project has structured prompt builders, state-to-prompt bridging, context budgeting, untrusted external context wrapping, semantic window summarization, and visible output leak prevention.

## Script Behavior

The script output should be compact and readable:

```text
QChat Engineering Map

[Harness]
  OK      QChat service adapter harness
  OK      Runtime readiness script

[Loop]
  OK      OneBot receive loop
  OK      QChat event queue loop

[Prompt]
  OK      XiaYu private state prompt
  OK      Semantic window summary

Summary: 32 passed, 0 missing
```

If a check fails:

```text
MISSING Prompt: XiaYu private state prompt marker
```

The script should use only PowerShell built-ins so it works on the existing Windows environment without dependency installation.

## Error Handling

Missing files and missing symbols should be reported independently. A missing file should not crash the script; it should mark all checks depending on that file as missing.

The script should resolve paths relative to the repository root, based on the script location. It should therefore work whether called from `D:\Alife` or another current directory.

## Verification

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-engineering-map.ps1
```

Expected result:

- Harness, Loop, and Prompt sections are printed.
- All required anchors are `OK`.
- Summary reports zero missing anchors.
- Process exits with code `0`.

Full `dotnet test` is not part of this change because this work is documentation plus static tooling, and previous full build/test attempts were blocked by NuGet repository-signature access (`NU1301`) rather than by project test failures.

## Future Work

After this map exists, the next useful increments are:

- Add prompt contract snapshot tests for private/internal prompt blocks.
- Add prompt leak tests across QQ text, TTS text, view text, and logs.
- Add loop invariant tests for at-most-once settled dispatch, bounded retries, and continuation gate decisions.
- Add runtime metrics for queue depth, settle pending count, warmup retry count, TTS readiness, vision readiness, state transitions, and continuation decisions.
- Add a consolidated runtime harness report that combines readiness, Loop anchors, Prompt anchors, and live endpoint status.
