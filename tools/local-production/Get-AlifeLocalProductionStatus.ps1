param([Parameter(Mandatory)][string]$StatusPath)
$ErrorActionPreference='Stop'
$status=Get-Content -LiteralPath $StatusPath -Raw|ConvertFrom-Json
[ordered]@{overall=$status.overall;accounts=$status.accounts;reason=$status.reason;observedAtUtc=$status.observedAtUtc}|ConvertTo-Json -Depth 6
