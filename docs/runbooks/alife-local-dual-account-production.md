# Alife local dual-account production

This Windows-only supervisor keeps `account-a` and `account-b` isolated. It exposes no listener and stores only safe health/reason state. Never place tokens in JSON; configure `ALIFE_ACCOUNT_A_ONEBOT_TOKEN` and `ALIFE_ACCOUNT_B_ONEBOT_TOKEN` as user environment variables.

## Bootstrap

1. Copy `config/local-production/accounts.example.json` to ignored `accounts.local.json` and verify distinct loopback ports and absolute roots.
   Discover the installed NapCat account slots without starting them using `powershell.exe -NoProfile -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat`; it must report exactly two ports and `started=false`.
2. Set `ALIFE_LOCAL_PRODUCTION_PLAN` to that local file.
   Preview OneBot Token state with `Initialize-NapCatDualAccountTokens.ps1 -NapCatRoot D:\NapCat`; after explicit authorization, add `-Apply` to generate distinct random Tokens, update both NapCat configs atomically, and set the two user environment variables. The command never prints Token values.
3. Validate without launch: `powershell.exe -NoProfile -File tools/local-production/Start-AlifeLocalSupervisor.ps1 -PlanPath config/local-production/accounts.local.json -Once -DryRun`.
4. Run mocked tests: `powershell.exe -NoProfile -File tools/local-production/Test-AlifeLocalSupervisor.ps1` and `Test-InstallAlifeLocalSupervisorTask.ps1`.
5. Install only after dry-run review: `Install-AlifeLocalSupervisorTask.ps1 -Install -PlanPath <local-plan>`.

## Operations

Read safe status with `Get-AlifeLocalProductionStatus.ps1 -StatusPath <status-file>`. Output is restricted to slot id, pid, health, failure/restart/drain/active counters and safe reason codes. `DependencyUnavailable`, `ConfigurationRejected`, `HealthProbeFailed`, `DeadlineExceeded`, `Busy`, and `RestartRecoveryRequired` never include secrets, chat/model text, SQL, stack traces, or absolute paths.

For an A-only incident, mark A draining, stop accepting A work, wait for active work to reach zero or its deadline, and restart only A. B must remain healthy and its SQLite queue must never be queried or migrated by A recovery.

## Acceptance drills

Record only safe before/after status and PASS/FAIL for: A+B baseline; disconnect A OneBot; A threshold restart; restart during finite A work; concurrent same-capability work; unavailable/start-timeout/health-fail adapters; supervisor restart recovery; and the observation window. Any cross-talk, unsafe output, restart storm, or failed row means not production ready.

## Current acceptance result (2026-07-11)

**NOT PRODUCTION READY.** Offline build and focused simulations pass, but live-drill prerequisites are absent on this machine: the ignored local two-account plan is not present, neither account Token environment variable is configured, and no NapCat/Alife process is running. No live process was started and no credential value was read. Prepare the two local accounts and repeat all eight drills; production readiness cannot be declared until every row passes and the observation window shows no restart storm, cross-talk, or unsafe notice.
