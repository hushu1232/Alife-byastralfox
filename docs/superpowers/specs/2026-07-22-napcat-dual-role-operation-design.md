# NapCat dual-role operation normalization

## Goal

Make the existing manifest-bound launcher the single repeatable way to stop and recover the two local NapCat roles, including the role-specific Quick-login argument that was previously corrupted to `System.Collections.Hashtable`.

## Scope

- Add `-Stop` to `tools/local-production/Start-NapCatDualAccount.ps1`.
- In `-Start -LoginMode Quick`, derive the role's numeric Quick-login account only from the same role root's enabled OneBot configuration for that role's manifest port.
- Correct only the `NapCatWinBootMain.exe` command line in that role's existing `napcat.quick.bat`, atomically and without returning the account value.
- Keep manifest discovery, QR behavior, OneBot port probes, and the existing safe JSON output.
- Add synthetic PowerShell coverage and document the two normal operations.

## Non-goals

- No automatic QR scan, cookie manipulation, token output, or account selection outside the matching role-local OneBot file.
- No new service, task scheduler entry, wrapper script, or dependency.
- No WebUI port redesign; the current shared `6099` management-port conflict is separate from Quick recovery and OneBot operation.

## Design

`Get-NapCatDualAccountPlan` remains the authority for exactly two manifests. While locating a role's enabled loopback WebSocket endpoint, it also validates that the matching `onebot11_<numeric-account>.json` filename has a numeric account suffix. The slot retains that value internally only; normal command output remains limited to safe ports and state.

Before a Quick start, the launcher calls one synchronizer per selected slot. It requires exactly one existing `NapCatWinBootMain.exe ...` line in the role's `napcat.quick.bat`, replaces that line with the internally resolved numeric account argument, and writes the replacement through a temporary sibling file. It makes no change when the line is already correct. A malformed wrapper or ambiguous configuration fails before any process is started.

`-Stop` is mutually exclusive with `-Start` and `-RestartLaunchers`. It stops only `QQ.exe` and `NapCatWinBootMain.exe` whose executable path is under a selected manifest's `RoleRoot`, tolerates child-process exit races, and reports only selected port and stopped state. It never enumerates or terminates the system QQ installation.

## Tests and acceptance

The synthetic fixture will contain a deliberately bad Quick command line and an enabled `onebot11_<numeric-account>.json` endpoint per role. The test first proves the synchronizer corrects only the role-local wrapper and then asserts neither wrapper retains `System.Collections.Hashtable`. It also proves a malformed/non-numeric config filename is rejected. No test starts QQ, NapCat, a browser, a listener, or a real account.

Acceptance is: (1) `-Stop` exits only role-bound processes, (2) Quick recovery repairs a corrupted command before launch, (3) discovery and QR behavior are unchanged, and (4) the standard output and documentation never disclose account values, tokens, cookies, or chat data.
