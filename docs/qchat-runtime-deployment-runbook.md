# QChat Runtime Deployment Runbook

## Purpose

This runbook prevents source/runtime mismatch for QChat. Source changes under `sources/Alife.Function/Alife.Function.QChat` do not automatically affect the live plugin under `Storage/Plugins/Alife.Function.QChat`.

Use this process when changing QChat behavior, intent detection, file handling, risk policy, owner event feedback, or dual-bot runtime wiring.

## Deployment Rule

Do not treat a source edit as live until all of these are true:

1. The solution builds.
2. QChat tests pass.
3. Changed plugin files are synced to `Storage/Plugins/Alife.Function.QChat`.
4. Alife is restarted when runtime-loaded files changed.
5. XiaYu and Mio reconnect to their OneBot/NapCat endpoints.
6. Live smoke cases pass for the changed behavior.

## Preflight

Check the worktree:

```powershell
git status --short
```

Expected:

- Review every modified file before deployment.
- Do not revert unrelated files.
- Confirm QChat changes are intentional.

Check the plan or task document:

```powershell
Get-Content -LiteralPath D:\Alife\docs\qchat-capability-matrix.md -TotalCount 40
```

Expected:

- The changed capability appears in the matrix.
- High-risk changes have an owner reporting path.

## Build

Run:

```powershell
dotnet build D:\Alife\Alife.slnx
```

Expected:

```text
Build succeeded.
```

If build fails:

- Do not sync plugin files.
- Fix build failures in source first.
- Re-run the build before continuing.

## Test

Run the QChat test project:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
```

Expected:

```text
Failed: 0
```

For focused work, run the focused test first, then the full QChat project:

```powershell
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore --filter QChatDecisionTraceTests
dotnet test D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj --no-restore
```

## Plugin Sync

Sync only the changed plugin source files that the runtime loads.

Source root:

```text
D:\Alife\sources\Alife.Function\Alife.Function.QChat
```

Runtime plugin root:

```text
D:\Alife\Storage\Plugins\Alife.Function.QChat
```

Manual example:

```powershell
Copy-Item -LiteralPath D:\Alife\sources\Alife.Function\Alife.Function.QChat\QChatIntentClassifier.cs -Destination D:\Alife\Storage\Plugins\Alife.Function.QChat\QChatIntentClassifier.cs -Force
Copy-Item -LiteralPath D:\Alife\sources\Alife.Function\Alife.Function.QChat\QChatService.cs -Destination D:\Alife\Storage\Plugins\Alife.Function.QChat\QChatService.cs -Force
```

Rules:

- Do not copy `bin`, `obj`, `Outputs`, runtime storage, or test files into the plugin directory.
- Do not assume all source files need copying.
- Copy new source files only when the runtime plugin compilation/load path needs them.
- If a new QChat source file is added, confirm the runtime plugin compiler includes it.

## Restart

Restart Alife when runtime-loaded plugin files changed.

Rules:

- Restart Alife, not NapCat, when QQ login/session is healthy.
- Restart NapCat only when the OneBot endpoint is broken, QR login expired, or NapCat itself is stale.
- After restart, confirm both bot endpoints connect.

Expected live endpoints:

- Mio endpoint: `127.0.0.1:3001`
- XiaYu endpoint: `127.0.0.1:3002`

Expected account identities:

- XiaYu BotId: `2905391496`
- Mio BotId: `3340947887`

## Live Smoke

Use:

```text
D:\Alife\docs\qchat-live-smoke-cases.md
```

Minimum smoke after intent or command changes:

- Owner `/qchat status` works.
- Non-owner cannot use owner diagnostics.
- "撤了吧" recalls when owner sends it.
- "他是不是不会撤回" does not recall.
- Image or forwarded metadata does not trigger file upload.
- Quiet/wake behavior matches authorization.

Minimum smoke after runtime/deployment changes:

- XiaYu connects.
- Mio connects.
- Each bot keeps its own identity.
- Owner event outbox can deliver or retain pending events.

## Failure Recovery

### Source Tests Passed But Live Behavior Is Old

Likely cause:

- Plugin files were not synced.
- Alife was not restarted.
- The wrong plugin directory was updated.

Action:

1. Check the modified source file timestamp.
2. Check the matching runtime plugin file timestamp.
3. Sync the file.
4. Restart Alife.
5. Re-run live smoke.

### One Bot Connected And The Other Failed

Likely cause:

- One NapCat endpoint is offline.
- One account needs QR login.
- One port changed.
- The OneBot WebSocket port is reachable, but the QQ account is offline inside NapCat.

Action:

1. Check endpoint `3001`.
2. Check endpoint `3002`.
3. Confirm BotId in logs.
4. Query OneBot `get_login_info` and `get_status`; `online=false` means the account session is not healthy even if TCP/WebSocket is reachable.
5. Restart only the failed side if possible.

Known local recovery case:

- Symptom: XiaYu `127.0.0.1:3002` was reachable and returned `get_login_info` for `2905391496`, but `get_status.online=false`; sending failed with NapCat `1006514 网络连接异常`.
- Recovery: stop only the XiaYu no-argument NapCat chain, then start XiaYu with quick-login account argument:

```powershell
Stop-Process -Id <xiaYuNapCatAndQQPids> -Force -ErrorAction SilentlyContinue
Start-Process -FilePath D:\NapCat\NapCat.Shell.Windows.OneKey-v4.18.6\NapCat.44498.Shell\NapCatWinBootMain.exe -ArgumentList @('2905391496') -WorkingDirectory D:\NapCat\NapCat.Shell.Windows.OneKey-v4.18.6\NapCat.44498.Shell -Verb RunAs
```

- Verification: `get_status.online=true` for both `3001` and `3002`, then run low-risk direct send diagnostics for both accounts.
- Do not stop the `-q 10086` chain when only XiaYu is unhealthy; that chain was the Mio `3001` runtime in the 2026-06-21 incident.

### NapCat QR Login Expired

Action:

1. Restart the affected NapCat shell.
2. Scan the QR code for the affected account.
3. Wait for OneBot endpoint to become available.
4. Restart Alife if it failed to reconnect.

### Plugin Copied But Build Failed

Action:

1. Stop deployment.
2. Fix source build.
3. Re-run tests.
4. Re-copy corrected plugin files.
5. Restart Alife.

### Long Task Completed While QQ Was Disconnected

Expected:

- Important owner feedback should be in owner event outbox.
- On reconnect, dispatcher should retry pending events.

Action:

1. Check owner event outbox state.
2. Confirm pending event dedupe key.
3. Restart dispatcher/runtime if needed.

## Acceptance Checklist

- [ ] `dotnet build D:\Alife\Alife.slnx` succeeded.
- [ ] Full QChat tests passed.
- [ ] Runtime plugin files were synced when needed.
- [ ] Alife was restarted when needed.
- [ ] XiaYu connected with correct BotId.
- [ ] Mio connected with correct BotId.
- [ ] Changed behavior passed live smoke.
- [ ] High-risk effects were reported to owner.
