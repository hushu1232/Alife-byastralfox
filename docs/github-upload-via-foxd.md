# Deprecated: GitHub Upload Via D:\FOXD

This old copy-based workflow is disabled for current development.

Do not upload Alife by copying tracked files into `D:\FOXD`, `D:\FOXD\alife-service`, or any FOXD worktree. Do not create new `Update Alife service snapshot` commits.

Current Alife uploads go only to:

```text
https://github.com/hushu1232/Alife-byastralfox
```

Use this current runbook:

```text
D:\Alife\docs\alife-upload-rules.md
```

Use this upload command after local verification and commit:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-to-github.ps1
```

The old snapshot-based master states are preserved only as archive branches:

```text
Alife-byastralfox:
backup/master-before-reconcile-20260628

ASRRAL-FOX:
backup/master-before-submodule-20260628
```

Treat those branches as read-only recovery points. Do not merge them back into active `master`.
