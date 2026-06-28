$ErrorActionPreference = "Stop"

throw @"
The old FOXD copy-based upload workflow is disabled.

Do not create Alife source snapshots under D:\FOXD.
Do not create "Update Alife service snapshot" commits.

Current Alife uploads must go only to:
  https://github.com/hushu1232/Alife-byastralfox

Use:
  powershell -ExecutionPolicy Bypass -File D:\Alife\tools\upload-alife-to-github.ps1

See:
  D:\Alife\docs\alife-upload-rules.md
"@
