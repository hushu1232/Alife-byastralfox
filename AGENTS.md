# AGENTS.md instructions for D:\Alife

## Default GitHub Upload Target

When the user asks to upload this project to GitHub without naming another destination, upload the current `D:\Alife` project to:

```text
git@github.com:hushu1232/Alife-byastralfox.git
```

Use the local remote name:

```text
alife-byastralfox
```

Do not replace `origin`. `origin` tracks the upstream BDFFZI Alife repository and is not the default upload target for this project.

## Current Repository Model

Alife and FOXD are separate repositories.

```text
D:\Alife
  -> canonical Alife .NET 9 source checkout
  -> uploads to Alife-byastralfox

D:\FOXD
  -> FOXD Web platform repository
  -> uploads to ASRRAL-FOX
  -> contains alife-service as a Git submodule

D:\FOXD\alife-service
  -> submodule checkout of Alife-byastralfox
  -> should point at a published Alife commit
```

Do not upload Alife into FOXD as a copied source snapshot. Do not create new `Update Alife service snapshot` commits.

## Normal Upload Workflow

1. Commit and verify Alife in `D:\Alife` first.
2. Push Alife to `alife-byastralfox`.
3. Verify the Alife commit exists on GitHub.
4. Update `D:\FOXD\alife-service` to that published commit.
5. Commit only the FOXD submodule gitlink update.
6. Push FOXD to the `github` remote.

Use the user-local .NET 9 SDK for Alife verification:

```powershell
& "C:\Users\hu shu\.dotnet\dotnet.exe" test "Tests\Alife.Test.Framework\Alife.Test.Framework.csproj" --filter "FullyQualifiedName~WebBridge"
```

The system `dotnet` may resolve to SDK 8 and cannot build the .NET 9 projects.

## Version Snapshot Rules

Use Git tags and GitHub Releases for version snapshots. Do not copy source snapshots into FOXD.

Alife stable version tag:

```powershell
git -C D:\Alife tag alife-vX.Y.Z <alife-commit>
git -C D:\Alife push alife-byastralfox alife-vX.Y.Z
```

Interview/demo checkpoint tag:

```powershell
git -C D:\Alife tag interview-snapshot-YYYY-MM-DD <alife-commit>
git -C D:\Alife push alife-byastralfox interview-snapshot-YYYY-MM-DD
```

## Deprecated Workflow

The old `D:\Alife\tools\upload-alife-service-via-foxd.ps1` copy-based snapshot workflow is deprecated for normal development.

It may only be used if the user explicitly asks to restore historical snapshot behavior for an archive branch.

Historical files under `docs/superpowers/` may still mention the old snapshot script. Treat those references as dated plan history only. This `AGENTS.md` file and `docs/alife-upload-rules.md` are the current authority.

Reference the current rules instead:

```text
D:\Alife\docs\alife-upload-rules.md
D:\FOXD\docs\alife-submodule-upload-rules.md
```

## Archive Branches

Old snapshot-style master states are preserved as read-only backup branches:

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
