# AGENTS.md instructions for D:\Alife

## Default GitHub Upload Target

When the user asks to upload this project to GitHub without naming another destination, upload the current `D:\Alife` project snapshot to:

```text
git@github.com:hushu1232/Alife-byastralfox.git
```

Use the local remote name `alife-byastralfox` for this repository. Do not replace `origin`; `origin` tracks the upstream source repository and may be behind/ahead.

Preferred workflow:

1. Commit the current `D:\Alife` work first, after verification.
2. Export the current `HEAD` file tree with `git archive`.
3. Update a temporary clone of `git@github.com:hushu1232/Alife-byastralfox.git` under `D:\tmp`.
4. Replace only tracked snapshot contents in that temporary clone; do not copy `.git`, `.codegraph`, `.worktrees`, `Outputs`, `Storage`, `Models`, `bin`, `obj`, or other generated/runtime data.
5. Commit in the temporary clone with a clear snapshot message.
6. Push `master:master` to GitHub and verify with `git ls-remote alife-byastralfox refs/heads/master`.
7. Remove the temporary clone after successful verification.

Rationale: direct full-history pushes from this working repository to `Alife-byastralfox` have previously failed on GitHub with a remote `index-pack` missing-object error. Snapshot upload avoids that old history/object-pack issue while keeping the GitHub repository current.

## GitHub Upload Via D:\FOXD

When the user asks to upload this project to GitHub "through D:\FOXD" or "using the previous upload spec", use the local runbook and script added for this workflow.

Primary workflow:

1. Use `D:\FOXD` as the GitHub carrier repository.
2. Use the GitHub remote named `github`, which points at `git@github.com:hushu1232/ASRRAL-FOX.git`.
3. Upload this project into the `alife-service` subtree of that repository.
4. Base the upload on the latest `github/master`, not the dirty `D:\FOXD` main worktree.
5. Create an isolated temporary worktree under `D:\tmp\alife-service-upload`.
6. Copy only files tracked by `git ls-files` in `D:\Alife`; do not copy `.git`, `.codegraph`, `.worktrees`, `Outputs`, `Runtime`, `Storage`, `Models`, `bin`, `obj`, or other generated/runtime data.
7. Stage only `alife-service`.
8. Commit with the message `Update Alife service snapshot`.
9. Push `HEAD:master` to the `github` remote.
10. Verify the remote with `git ls-remote github refs/heads/master`.
11. Remove the temporary worktree after successful verification.

Preferred command:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-service-via-foxd.ps1
```

Reference documentation:

```text
D:\Alife\docs\github-upload-via-foxd.md
```

Important constraints:

- Do not commit unrelated dirty files in `D:\FOXD`.
- Do not use the `D:\FOXD` main worktree for the upload commit.
- Do not mirror build outputs or runtime state.
- If a sandbox permission error blocks the workflow, request escalation for the exact command being run and use a narrow prefix rule.
- Network pushes and remote verification require SSH access to GitHub.
