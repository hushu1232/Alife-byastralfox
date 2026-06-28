# GitHub Upload Via D:\FOXD

This old snapshot workflow is deprecated.

FOXD now references Alife through a Git submodule at `D:\FOXD\alife-service`. Alife must be uploaded to its own repository first:

```text
git@github.com:hushu1232/Alife-byastralfox.git
```

Then FOXD should commit only the `alife-service` submodule pointer update.

Do not use the previous copy-based workflow for normal development:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-service-via-foxd.ps1
```

That workflow copied tracked files from `D:\Alife` into `D:\FOXD\alife-service` as a normal directory snapshot and created commits named `Update Alife service snapshot`. It caused FOXD and Alife history to diverge and conflicts with the current submodule model.

Use this canonical runbook instead:

```text
D:\Alife\docs\alife-upload-rules.md
D:\FOXD\docs\alife-submodule-upload-rules.md
```

The old snapshot-based master states are preserved only as archive branches:

```text
Alife-byastralfox:
backup/master-before-reconcile-20260628

ASRRAL-FOX:
backup/master-before-submodule-20260628
```

Treat those branches as read-only recovery points. Do not merge them back into active `master`.
