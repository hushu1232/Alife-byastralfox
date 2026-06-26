# QChat Harness, Loop, and Prompt Engineering

This document maps the engineering practices that keep QChat understandable, testable, and operationally stable.

QChat currently has three visible engineering layers:

- Harness engineering validates runtime behavior without requiring the full live stack for every check.
- Loop engineering drives the long-running agent runtime, event queues, semantic settling, state transitions, retries, and continuation decisions.
- Prompt engineering turns persona, state, tool context, visual context, and safety rules into structured model input while preventing private/internal blocks from leaking to visible output.

## Harness Engineering

Harness engineering is the project's test and operations scaffold. It gives QChat a way to validate behavior with fake runtimes, focused tests, readiness scripts, and live smoke boundaries.

Key anchors:

- `Tests/Alife.Test.QChat/QChatServiceAdapterTests.cs`: QChat service harness with `CreateStartedService`, fake OneBot runtime, fake image recognition, and service-level behavior checks.
- `Tests/Alife.Test.QChat/QChatVisionReadinessTests.cs`: vision readiness tests.
- `Tests/Alife.Test.QChat/QChatVoiceWarmupCoordinatorTests.cs`: TTS warmup and retry behavior tests.
- `Tests/Alife.Test.QChat/QChatModelReplyLoopLiveTests.cs`: live model reply loop boundary tests.
- `tools/check-qchat-runtime-readiness.ps1`: operational readiness check for the live QChat stack.

Maturity: medium-high. The project has strong local harnesses and live readiness entry points. The main gap is that these checks are spread across tests, scripts, and docs rather than surfaced through one top-level harness report.

Recommended next steps:

- Add a consolidated runtime harness report.
- Include readiness, endpoint status, Loop anchors, Prompt anchors, and live smoke test availability in one command.
- Keep live tests clearly separated from static and deterministic tests.

## Loop Engineering

Loop engineering is the runtime skeleton of QChat. It covers event intake, queue consumption, periodic updates, delayed semantic dispatch, state-machine feedback, retry/warmup behavior, and continuation decisions.

Key anchors:

- `sources/Alife.Function/Alife.Function.QChat/OneBotClient.cs`: `ReceiveLoop` reads OneBot WebSocket events while the socket remains open.
- `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`: `ProcessOneBotEventQueueAsync` consumes queued OneBot events.
- `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`: `ITimeIterative.OnUpdate` connects QChat to the runtime update loop.
- `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`: `ScheduleSettledDispatch` and `DispatchSettledConversationAsync` implement semantic settle dispatch.
- `sources/Alife.Function/Alife.Function.QChat/QChatService.cs`: `EnableContinuationGate` controls model continuation after deterministic task feedback.
- `sources/Alife.Function/Alife.Function.QChat/QChatVoiceWarmupCoordinator.cs`: background TTS warmup and retry coordination.
- `sources/Alife.Function/Alife.Function.QChat/QChatContinuationPolicy.cs`: continuation decision policy.
- `sources/Alife.Function/Alife.Function.QChat/XiaYuSelfStateMachine.cs`: `XiaYuSelfStateMachine.Apply` updates private character state from event frames.
- `sources/Alife.Function/Alife.Function.QChat/QChatOwnerEventDispatcher.cs`: owner-event outbox flushing and retry boundary.

Runtime flow:

1. OneBot receives QQ/NapCat events.
2. QChat queues and processes inbound events.
3. Conversation messages may wait in the semantic settle window.
4. The settled frame updates persona/state-machine context.
5. Prompt builders compose model input.
6. The model, deterministic tools, TTS, vision, and owner-event systems produce outputs.
7. Continuation policy decides whether tool feedback should trigger another model pass.

Maturity: high. The core architecture is loop-oriented rather than single request/response. The main gap is observability: queue depth, settle pending count, warmup attempts, continuation decisions, and state transitions should be surfaced as first-class metrics.

Recommended next steps:

- Add loop invariant tests for at-most-once settled dispatch and bounded retries.
- Add metrics for queue depth, settle windows, TTS readiness, vision readiness, and continuation decisions.
- Document owner/non-owner safety invariants across the event loop.

## Prompt Engineering

Prompt engineering is the model interface layer. It converts runtime state, persona, context, visual analysis, and safety boundaries into structured prompt blocks.

Key anchors:

- `QChatService.RegisterStablePersonaPromptIfNeeded`: stable persona prompt registration.
- `QChatPersonaIntensityPromptFormatter`: persona intensity and safety boundary prompt formatting.
- `QChatService.FormatPersonaFramePrompt`: persona frame prompt block.
- `QChatConversationCognition.BuildInternalPrompt`: internal conversation cognition prompt.
- `QChatService.BuildAddressPrompt`: addressing prompt.
- `QChatService.BuildQuietModeAcknowledgementPrompt`: quiet-mode state prompt.
- `XiaYuStatePromptFormatter`: XiaYu private state prompt.
- `QChatSemanticWindowSummary`: semantic settle-window summary prompt.
- `ExternalContextFormatter.WrapUntrusted`: untrusted external context wrapper.
- `ContextBudgetComposer`: context budget composition.
- `QChatVisibleTextPolicy`, `QChatVisibleReplyPolicy`, and `QChatExperienceSanitizer`: visible output sanitization and private prompt leak prevention.

Important prompt blocks:

- `[qchat persona frame]`
- `[XiaYu state - private, do not quote]`
- `[semantic_window]`
- `[untrusted_image_analysis]`

Maturity: medium-high to high. Prompt construction is structured, tested in several areas, and tied to state-machine output. The main gap is contract management: prompt block versions, snapshot tests, and leak tests should be more explicit.

Recommended next steps:

- Add prompt contract snapshot tests for private state, semantic window, persona frame, and image analysis blocks.
- Add leak tests across QQ visible text, TTS text, view text, and logs.
- Add prompt contract IDs or versions for long-lived internal blocks.

## Static Check

Run the static engineering map check with:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-engineering-map.ps1
```

The script does not start services, read API keys, call external networks, or modify runtime state. It only verifies that expected files and symbols still exist.

Expected result:

- Harness section prints all expected anchors.
- Loop section prints all expected anchors.
- Prompt section prints all expected anchors.
- Summary reports zero missing anchors.
- Exit code is `0`.

## Current Assessment

Harness is the validation shell, Loop is the runtime skeleton, and Prompt is the model interface layer.

The project already demonstrates all three. The most useful improvement is not inventing these concepts from scratch, but making them explicit, observable, and contract-tested.
