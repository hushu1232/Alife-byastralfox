# GitHub Upload Via D:\FOXD

This is the canonical local process for uploading `D:\Alife` to GitHub through the existing `D:\FOXD` repository.

## Purpose

`D:\FOXD` is the carrier repository for `git@github.com:hushu1232/ASRRAL-FOX.git`. The Alife project is uploaded into the `alife-service` subtree of that repository.

The workflow must avoid the dirty `D:\FOXD` main worktree. It uses a temporary worktree based on the latest GitHub `master`, copies a clean tracked-file snapshot from `D:\Alife`, commits only `alife-service`, pushes, verifies the remote, and cleans up.

## One-Command Upload

Run this from any directory:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-service-via-foxd.ps1
```

The script defaults are:

```text
Source project: D:\Alife
Carrier repo:   D:\FOXD
Subtree:        alife-service
Remote:         github
Branch:         master
Temp worktree:  D:\tmp\alife-service-upload
Commit message: Update Alife service snapshot
```

## Exact Flow

1. Fetch `github` in `D:\FOXD`.
2. Create `D:\tmp\alife-service-upload` from `github/master`.
3. Compare tracked files from `D:\Alife` with tracked files under `alife-service`.
4. Remove target tracked files that no longer exist in `D:\Alife`.
5. Copy only files returned by `git -C D:\Alife ls-files`.
6. Run `git add -A -- alife-service`.
7. If nothing is staged, stop without creating a commit.
8. Commit with `Update Alife service snapshot`.
9. Push with `git push github HEAD:master`.
10. Verify with `git ls-remote github refs/heads/master`.
11. Remove the temporary worktree.

## What Must Not Be Uploaded

The script intentionally does not copy untracked files. This excludes:

- `.git`
- `.codegraph`
- `.worktrees`
- `Outputs`
- `Runtime`
- `Storage`
- `Models`
- `bin`
- `obj`
- local caches
- generated build output
- local runtime state

If a new file should be part of the GitHub snapshot, first make it a tracked file in `D:\Alife`.

## Permission Requirements

The full workflow may need elevated approval for these categories:

- writing and removing `D:\tmp\alife-service-upload`
- writing Git worktree metadata under `D:\FOXD\.git\worktrees`
- running `git add` and `git commit` from the temporary worktree
- SSH network access for `git push`
- SSH network access for `git ls-remote`

When approval is needed, request narrow prefix rules for the specific command family. Do not request broad arbitrary PowerShell or Python approval.

## Verification Checklist

Before saying the upload is complete, verify:

```powershell
git -C D:\FOXD rev-parse github/master
git -C D:\FOXD ls-remote github refs/heads/master
git -C D:\FOXD worktree list
```

Expected state:

- `github/master` points at the pushed commit.
- `refs/heads/master` on GitHub points at the pushed commit.
- `D:\tmp\alife-service-upload` is absent from `git worktree list`.
- Dirty unrelated files in `D:\FOXD` remain untouched.
