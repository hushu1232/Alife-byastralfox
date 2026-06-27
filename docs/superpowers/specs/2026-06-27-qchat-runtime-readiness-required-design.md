# QChat Runtime Readiness Required Gate Design

## Goal

Promote QChat runtime readiness from an optional engineering-map entry to a required gate. The gate must prove that the repository contains a stable, executable readiness check for QChat vision and voice runtime prerequisites, while keeping normal development and CI independent from local live services.

## Non-Goals

This slice does not start GPT-SoVITS, NapCat, or any other external process. It does not create a new plugin module yet, change QChat state-machine behavior, enable vision or voice by default, or require real API keys during normal tests. Live runtime availability is checked only when the readiness script is run in explicit live strict mode.

## Recommended Approach

Use a contract-required and live-strict model.

Default mode verifies that the runtime readiness script exists, has stable output, checks the expected QChat runtime prerequisites, and can report missing local dependencies without breaking normal engineering-map execution. Live strict mode performs the real machine check and exits non-zero when required live prerequisites are missing.

This avoids a brittle default gate that fails whenever the developer has not started local TTS endpoints, while still giving a single command for hard runtime validation before launching the bot.

## Runtime Readiness Script

`tools/check-qchat-runtime-readiness.ps1` becomes a real gate script with explicit modes:

- Default mode: contract/readiness capability check. It must run without requiring live endpoints or user-level secrets.
- `-Live`: probe live dependencies such as local TTS endpoints and configured reference audio.
- `-Strict`: treat missing required live prerequisites as failures and exit 1.
- `-Json`: optionally emit machine-readable status for future UI or automation reuse.

The human-readable output should be stable and grouped:

```text
QChat Runtime Readiness
[Vision]
  PASS|WARN|MISSING Agnes vision API key
[Voice]
  PASS|WARN|MISSING Xiayu TTS endpoint 9880
  PASS|WARN|MISSING Mixu TTS endpoint 9881
  PASS|WARN|MISSING Xiayu reference audio
  PASS|WARN|MISSING Mixu reference audio
[Summary]
  Summary: X required passed, Y required missing, Z warnings
```

Default mode may report live dependency warnings, but it must not fail only because `127.0.0.1:9880`, `127.0.0.1:9881`, or `ALIFE_AGNES_VISION_API_KEY` are absent. `-Live -Strict` must fail for missing required live prerequisites.

## Required Checks

The script must keep checking these runtime prerequisites:

- Agnes vision API key resolution from user or process environment.
- Xiayu GPT-SoVITS endpoint at `127.0.0.1:9880`.
- Mixu GPT-SoVITS endpoint at `127.0.0.1:9881`.
- Xiayu Chinese and Japanese reference audio paths.
- Mixu Chinese and Japanese reference audio paths.

The script should preserve stable field names already used by the engineering map, including:

- `AgnesVisionKeyConfigured`
- `XiayuTts9880Reachable`
- `MixuTts9881Reachable`
- `XiayuZhRef`
- `XiayuJaRef`
- `MixuZhRef`
- `MixuJaRef`

## Engineering Map Changes

`tools/check-qchat-engineering-map.ps1` should promote `Runtime readiness script` from optional to required. The marker list should prove that the script contains the expected contract:

- `QChat Runtime Readiness`
- `AgnesVisionKeyConfigured`
- `XiayuTts9880Reachable`
- `MixuTts9881Reachable`
- `-Live`
- `-Strict`
- `exit 1`

The expected engineering-map summary should move from:

```text
30 required passed, 0 required missing, 1 optional present, 0 optional missing
```

to:

```text
31 required passed, 0 required missing, 0 optional present, 0 optional missing
```

If implementation splits runtime readiness into multiple required entries, the required count may be higher, but there should be no remaining optional runtime-readiness entry.

## Tests

Add focused tests in `Tests/Alife.Test.QChat` rather than broad live integration tests. The tests should cover:

- `Runtime readiness script` is declared required in the engineering map.
- The script contains live and strict mode markers.
- The script keeps the stable readiness field names.
- Default mode exits 0 in a normal developer environment even when live services are absent.
- Strict live mode can be proven to exit non-zero under an isolated missing-prerequisite scenario without depending on real user secrets.

The existing full `.NET 9` test suite and the engineering-map script must continue to pass.

## Error Handling

Missing API keys, unreachable TTS endpoints, and missing reference audio should produce stable reason strings. Default mode reports these as warnings or non-fatal live readiness gaps. `-Live -Strict` reports them as missing required prerequisites and exits 1.

Unexpected script errors should fail fast with a non-zero exit code. The script should not silently convert PowerShell exceptions into successful readiness output.

## Future Module Boundary

After this required gate is stable, a later slice can open a dedicated runtime module such as `Alife.Function.QChatRuntime` or add a C# `QChatRuntimeReadiness` service. That module can centralize account capability registry, multimodal readiness, startup warmup coordination, and UI/API status projection.

This design intentionally keeps the first slice smaller: make the runtime readiness gate reliable before moving the logic into a reusable plugin module.

## Verification Plan

Run these checks before claiming completion:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-runtime-readiness.ps1
powershell -ExecutionPolicy Bypass -File tools/check-qchat-engineering-map.ps1
& 'C:\Users\hu shu\.dotnet\dotnet.exe' test Alife.slnx --no-restore --no-build -v:minimal
git diff --check
```

For live machine validation, run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-qchat-runtime-readiness.ps1 -Live -Strict
```

That live command is expected to fail until Agnes vision key, TTS endpoints, and reference audio prerequisites are actually available on the machine.
