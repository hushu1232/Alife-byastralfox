# NapCat role startup hardening

## Goal

Make the local two-role launcher report real per-role startup state instead of treating a successful `Start-Process` call as a usable QQ/NapCat session.

## Scope

- Discover exactly two roles from `D:\NapCat\*\alife-napcat-role-host.json`.
- Bind every role's root, launch path, OneBot port, and login entrypoint to the same manifest root.
- Keep quick recovery and QR login as explicit modes.
- Report host process, loopback port, and authenticated OneBot `get_status` separately for each role.
- Keep access tokens in existing user environment variables; never print, persist, or return them.

## Non-goals

- No automatic QR scan, account selection, cookie manipulation, process-wide QQ termination, or DataAgent/LangGraph participation.
- No concurrent QR windows: the installed QQ client has one interactive desktop session, so QR mode requires one `-AccountPort` at a time.
- No new dependency or supervisor redesign.

## Options considered

1. Keep the global recursive lookup and add retries. Rejected: it can associate a config from one role root with a launcher from another root.
2. Use the two role manifests as the source of truth. Recommended: the manifests already define each role root, exact host executable, and OneBot port.
3. Replace local startup with a new service. Rejected: it does not fix the current launcher/config mismatch and adds unnecessary lifecycle complexity.

## Design

`Start-NapCatDualAccount.ps1` will derive two slots only from role manifests. It will reject duplicate ports, a non-loopback host, a missing quick wrapper, or a missing QR launcher under that role root.

`-LoginMode Quick` remains the default. It starts the role's existing `napcat.quick.bat`, which is the established cached-session path. Its process check matches the manifest's exact `LaunchPath`, never a globally discovered executable.

`-LoginMode Qr` requires `-AccountPort 3001` or `-AccountPort 3002` and `-Interactive`. It starts that role root's `launcher-win10-user.bat` without `-q`. It reports `qrRequested`; it does not report the account as online merely because a launcher was created.

After a start request, each slot is reported independently:

| Field | Meaning |
| --- | --- |
| `hostRunning` | A `NapCatWinBootMain.exe` exists at the manifest's exact launch path. |
| `portReachable` | The configured loopback TCP port accepts a connection before the short timeout. |
| `oneBotStatus` | `online`, `offline`, or `unknown`, from authenticated OneBot `get_status`; missing token, timeout, or invalid response is `unknown`. |
| `ready` | True only when `hostRunning`, `portReachable`, and `oneBotStatus=online`. |

The OneBot probe reads only the existing account-specific user environment token. It uses it only in the local WebSocket authorization request, never emits it, and returns a safe status code when authentication or the protocol probe fails.

## Tests

Extend `Test-NapCatDualAccountDiscovery.ps1` with two distinct synthetic role roots. Verify that each returned slot keeps its own root, executable, and port rather than sharing the first launcher found on disk. Add pure status-classification checks for online, offline, and malformed OneBot results. No test starts QQ, NapCat, WebSocket listeners, or real accounts.

## Acceptance

1. Quick mode never reports ready if the host, port, or OneBot online probe is missing.
2. QR mode refuses dual-role launch without an explicit single port.
3. A report identifies which role failed and whether the failure is host, port, or online state, without exposing credentials or chat data.
