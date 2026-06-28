# AGENTS.md instructions for D:\Alife

## Default GitHub Upload Target

When the user asks to upload this project to GitHub without naming another destination, upload the current `D:\Alife` project only to:

```text
https://github.com/hushu1232/Alife-byastralfox
```

Use the local remote name:

```text
alife-byastralfox
```

The remote may use SSH for authentication:

```text
git@github.com:hushu1232/Alife-byastralfox.git
```

Do not replace `origin`. `origin` tracks the upstream BDFFZI Alife repository and is not the default upload target for this project.

## Current Repository Model

`D:\Alife` is the canonical Alife .NET 9 source checkout.

Alife upload is now a single-repository workflow:

```text
D:\Alife -> alife-byastralfox/master
```

Do not touch `D:\FOXD`, `D:\FOXD\alife-service`, or the ASRRAL-FOX repository during an Alife GitHub upload. Do not create local FOXD source copies, source mirrors, or copied-source service commits.

If FOXD integration work is needed later, treat it as a separate explicit task. It is not part of the Alife upload flow.

## Normal Upload Workflow

1. Work in `D:\Alife` or a dedicated Alife worktree.
2. Verify the intended source changes in `D:\Alife`.
3. Commit the Alife changes locally.
4. Push only to `alife-byastralfox`.
5. Verify `alife-byastralfox/master` points at the pushed commit.

Preferred command:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-to-github.ps1
```

Manual equivalent:

```powershell
git -C D:\Alife status --short --branch
git -C D:\Alife push alife-byastralfox HEAD:master
git -C D:\Alife ls-remote --heads alife-byastralfox master
```

Use the user-local .NET 9 SDK for Alife verification:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test Alife.slnx --no-restore --no-build -v:minimal
```

The system `dotnet` may resolve to SDK 8 and cannot build the .NET 9 projects.

## Version Tag Rules

Use Git tags and GitHub Releases for version checkpoints. Do not copy source checkpoints into FOXD.

Alife stable version tag:

```powershell
git -C D:\Alife tag alife-vX.Y.Z <alife-commit>
git -C D:\Alife push alife-byastralfox alife-vX.Y.Z
```

Interview/demo checkpoint tag:

```powershell
git -C D:\Alife tag interview-checkpoint-YYYY-MM-DD <alife-commit>
git -C D:\Alife push alife-byastralfox interview-checkpoint-YYYY-MM-DD
```

## Deprecated FOXD Snapshot Workflow

The old `D:\Alife\tools\upload-alife-service-via-foxd.ps1` copy-based workflow is disabled for current development.

Do not create new `Update Alife service snapshot` commits. Do not upload Alife by copying tracked files into `D:\FOXD` or any FOXD subfolder.

Historical files under `docs/superpowers/` may still mention the old copy-based workflow. Treat those references as dated plan history only. This `AGENTS.md` file and `docs/alife-upload-rules.md` are the current authority.

Reference the current rules instead:

```text
D:\Alife\docs\alife-upload-rules.md
```

## Archive Branches

Old copy-based master states are preserved as read-only backup branches:

```text
Alife-byastralfox:
backup/master-before-reconcile-20260628

ASRRAL-FOX:
backup/master-before-submodule-20260628
```

Do not merge these archive branches back into active `master` unless the user explicitly requests historical recovery.

## Files That Must Not Be Uploaded

Never commit or copy these as part of an Alife upload:

- `.git`
- `.codegraph`
- `.codex`
- `.playwright-mcp`
- `.superpowers`
- `.tmp`
- `.worktrees`
- `Outputs`
- `output`
- `Runtime`
- `Storage`
- `Temp`
- `Models`
- `bin`
- `obj`
- local logs
- local screenshots
- generated package staging directories
- credentials, access tokens, or local account state

If an artifact is needed for release or demo, publish it as a GitHub Release asset or store it outside Git.

## Force Push Rule

Never use plain `--force` for `master`.

If a future cleanup requires replacing `master`, first create a backup branch for the current remote `master`, verify the target commit, then use:

```powershell
git push --force-with-lease=master:<expected-old-master-commit> <remote> <source>:master
```

If the lease fails, stop and re-check the remote state.
