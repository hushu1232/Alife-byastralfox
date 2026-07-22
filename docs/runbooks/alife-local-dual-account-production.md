# Alife local dual-account production

This Windows-only supervisor keeps `account-a` and `account-b` isolated. It exposes no listener and stores only safe health/reason state. Never place tokens in JSON; configure `ALIFE_ACCOUNT_A_ONEBOT_TOKEN` and `ALIFE_ACCOUNT_B_ONEBOT_TOKEN` as user environment variables.

## Bootstrap

1. Copy `config/local-production/accounts.example.json` to ignored `accounts.local.json` and verify distinct loopback ports and absolute roots.
   Discover the installed NapCat account slots without starting them using `powershell.exe -NoProfile -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat`; it must report exactly two ports and `started=false`.
2. Set `ALIFE_LOCAL_PRODUCTION_PLAN` to that local file.
   Preview OneBot Token state with `powershell.exe -NoProfile -File tools/local-production/Initialize-NapCatDualAccountTokens.ps1 -NapCatRoot D:\NapCat`; after explicit authorization, add `-Apply` to generate distinct random Tokens, update both NapCat configs atomically, and set the two user environment variables. The command never prints Token values.
3. Validate without launch: `powershell.exe -NoProfile -File tools/local-production/Start-AlifeLocalSupervisor.ps1 -PlanPath config/local-production/accounts.local.json -Once -DryRun`.
4. Run mocked tests: `powershell.exe -NoProfile -File tools/local-production/Test-AlifeLocalSupervisor.ps1` and `powershell.exe -NoProfile -File tools/local-production/Test-InstallAlifeLocalSupervisorTask.ps1`.
5. Install only after dry-run review: `powershell.exe -NoProfile -File tools/local-production/Install-AlifeLocalSupervisorTask.ps1 -Install -PlanPath <local-plan>`.

## NapCat launch modes

Discover the two manifest-bound role slots without starting NapCat:

```powershell
powershell.exe -NoProfile -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat
```

The discovery result must show `accountCount=2`, ports `3001` and `3002`, and `started=false`. It never starts a QQ or NapCat process.

Stop only the two manifest-bound local role instances:

```powershell
powershell.exe -NoProfile -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat -Stop
```

`-Stop` is limited to `QQ.exe` and `NapCatWinBootMain.exe` under the selected role roots. It does not terminate the system QQ installation, remove cached login state, or change OneBot configuration.

Use cached-session recovery for both roles:

```powershell
powershell.exe -NoProfile -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat -Start -LoginMode Quick
```

Add `-AccountPort 3001` or `-AccountPort 3002` to recover one role only. Before Quick mode requests the existing role-local quick launcher, it synchronizes only that wrapper's `NapCatWinBootMain.exe` argument from the matching role-local enabled OneBot configuration. The command never returns the resolved account, token, cookie, or chat data. Quick mode does not itself prove that QQ is logged in.

Request one visible QR login flow:

```powershell
powershell.exe -NoProfile -File tools/local-production/Start-NapCatDualAccount.ps1 -NapCatRoot D:\NapCat -Start -AccountPort 3001 -LoginMode Qr -Interactive
```

QR mode always requires one explicit account port and `-Interactive`; run the command separately for the other role. The output sets `qrRequested=true` but does not claim that the role is ready until its runtime checks succeed.

For a start request, treat only `ready=true` as usable. It requires `hostRunning=true`, `portReachable=true`, and `oneBotStatus=online`. `online` and `offline` come from OneBot `get_status`; `unknown` means the token, connection, or response could not verify QQ state and must not be treated as either online or offline.

## Character instances

Validate the existing local character sources without changing either account:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Install-AlifeDualAccountCharacters.ps1
```

Install the complete instances into their isolated account Storage roots:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/local-production/Install-AlifeDualAccountCharacters.ps1 -Install
```

The fixed mapping is account A to the `真央` instance (presented by QChat as 咪绪) and account B to the `夏羽` instance. Account A's Alife window lists only 真央 and account B's window lists only 夏羽; one window does not aggregate both accounts. The installer does not start, stop, or restart Alife, NapCat, or QQ. Restart and activation require separate owner authorization.

### Local persona context

Complete approved persona Markdown stays only in the matching local `Storage/Character/<character>/Memory/Persona` directory. QChat loads it for disclosure protection but never appends the complete file to `ChatHistory` or sends its path/body to QQ. When a current reply genuinely needs it, the normal model route may request one bounded approved fact category; C# validates the request, reads only that category, and asks the model for a natural final reply. Persona storage remains local runtime state and must not be committed, copied between accounts, or treated as a DataAgent/LangGraph execution input.

## Operations

Read safe status with `powershell.exe -NoProfile -File tools/local-production/Get-AlifeLocalProductionStatus.ps1 -StatusPath <status-file>`. Output is restricted to slot id, pid, health, failure/restart/drain/active counters and safe reason codes. `DependencyUnavailable`, `ConfigurationRejected`, `HealthProbeFailed`, `DeadlineExceeded`, `Busy`, and `RestartRecoveryRequired` never include secrets, chat/model text, SQL, stack traces, or absolute paths.

For an A-only incident, mark A draining, stop accepting A work, wait for active work to reach zero or its deadline, and restart only A. B must remain healthy and its SQLite queue must never be queried or migrated by A recovery.

## Acceptance drills

Record only safe before/after status and PASS/FAIL for: A+B baseline; disconnect A OneBot; A threshold restart; restart during finite A work; concurrent same-capability work; unavailable/start-timeout/health-fail adapters; supervisor restart recovery; and the observation window. Any cross-talk, unsafe output, restart storm, or failed row means not production ready.

## Current acceptance result (2026-07-11)

**NOT PRODUCTION READY.** Offline build and focused simulations pass. The ignored local plan now maps two installed NapCat accounts to loopback ports 3001/3002; distinct OneBot Tokens are configured, authenticated WebSocket handshakes passed, and two isolated Alife workers started. Live baseline passed. The A-only disconnect isolated port 3001 while port 3002 stayed available, but automated A quick-login recovery did not restore port 3001 within the observation deadline. Account A requires interactive QQ login confirmation before the recovery drill can be repeated; remaining live drills are not yet accepted. No credential value or account number was recorded.
