# Alife Upload Rules

## Current Repository Model

`D:\Alife` is the canonical local checkout for the Alife .NET 9 runtime.

Default upload target:

```text
git@github.com:hushu1232/Alife-byastralfox.git
```

Use the local remote name:

```text
alife-byastralfox
```

Do not replace `origin`. `origin` tracks the upstream BDFFZI Alife repository and is not the default upload target for this project.

FOXD references Alife through a Git submodule:

```text
D:\FOXD\alife-service -> git@github.com:hushu1232/Alife-byastralfox.git
```

The submodule pointer in FOXD should be updated only after the target Alife commit exists on `Alife-byastralfox`.

## Hard Rules

- Do not upload Alife into FOXD as a copied source snapshot.
- Do not create new `Update Alife service snapshot` commits.
- Do not use the old `D:\Alife\tools\upload-alife-service-via-foxd.ps1` snapshot workflow unless explicitly restoring historical behavior for an archive branch.
- Do not commit runtime state, build output, generated package output, model weights, logs, local caches, or credentials.
- Do not push to `origin` unless the user explicitly asks to interact with the upstream BDFFZI repository.
- Do not force-push `master` unless a backup branch exists and the command uses `--force-with-lease` with the expected old remote commit.

## Normal Alife Upload Flow

1. Work in `D:\Alife` or an Alife worktree.
2. Check status:

   ```powershell
   git -C D:\Alife status --short --branch --untracked-files=no
   ```

3. Run focused verification with the user-local .NET 9 SDK:

   ```powershell
   & "C:\Users\hu shu\.dotnet\dotnet.exe" test "Tests\Alife.Test.Framework\Alife.Test.Framework.csproj" --filter "FullyQualifiedName~WebBridge"
   ```

4. Commit the Alife changes:

   ```powershell
   git -C D:\Alife add <paths>
   git -C D:\Alife commit -m "<clear message>"
   ```

5. Push Alife first:

   ```powershell
   git -C D:\Alife push alife-byastralfox master
   ```

6. Verify the remote:

   ```powershell
   git -C D:\Alife ls-remote --heads alife-byastralfox master
   ```

7. Only after this succeeds, update FOXD's `alife-service` submodule pointer.

## Updating FOXD After Alife Upload

After the Alife commit exists on GitHub:

```powershell
git -C D:\FOXD\alife-service fetch origin
git -C D:\FOXD\alife-service checkout <published-alife-commit>
git -C D:\FOXD add alife-service
git -C D:\FOXD commit -m "chore: update Alife submodule pointer"
git -C D:\FOXD push github master
```

The FOXD commit should contain only the submodule gitlink change unless FOXD Web files were intentionally changed too.

## Local Folder Roles

`D:\Alife` is the Alife source-of-truth working folder.

`D:\FOXD\alife-service` is the FOXD submodule checkout. It is allowed to be detached at a specific commit. Detached HEAD inside a submodule is normal.

If a change is accidentally made inside `D:\FOXD\alife-service`, move or cherry-pick it into `D:\Alife`, push it to `Alife-byastralfox`, then update the FOXD pointer. Do not leave the only copy of an Alife change inside the submodule checkout.

## Version Snapshot Policy

Use tags for source snapshots.

Stable runtime version:

```powershell
git -C D:\Alife tag alife-vX.Y.Z <alife-commit>
git -C D:\Alife push alife-byastralfox alife-vX.Y.Z
```

Interview or demo checkpoint:

```powershell
git -C D:\Alife tag interview-snapshot-YYYY-MM-DD <alife-commit>
git -C D:\Alife push alife-byastralfox interview-snapshot-YYYY-MM-DD
```

Use GitHub Releases or external artifact storage for binaries, generated builds, model packages, and large runtime assets. Do not put those files in Git history.

## Existing Archive Branches

Old snapshot-style master states were preserved as archives:

```text
Alife-byastralfox:
backup/master-before-reconcile-20260628

ASRRAL-FOX:
backup/master-before-submodule-20260628
```

Treat these branches as read-only recovery points. Do not merge them back into active `master`.

Historical files under `docs/superpowers/` may still mention the old copy-based snapshot workflow. Treat those references as historical notes only. This document and `AGENTS.md` are the current upload authority.

## Cleanup Boundaries

Never upload these paths or file categories as part of an Alife source commit:

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

If any generated artifact is needed for a demo, publish it as a release asset or store it outside Git.

## .NET SDK Rule

This project targets .NET 9. On this machine, use:

```text
C:\Users\hu shu\.dotnet\dotnet.exe
```

The system `dotnet` may resolve to `C:\Program Files\dotnet\dotnet.exe` and only see SDK 8. That executable cannot build or test the Alife .NET 9 projects.
