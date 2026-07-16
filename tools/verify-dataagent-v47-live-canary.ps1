param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$allowed = [string[]]@(
    "live_canary_closure", "source_baseline", "accepted", "reason_code",
    "observation_capacity", "observation_window_minutes", "observation_count",
    "accepted_count", "rejected_count", "fallback_count", "timeout_count",
    "unavailable_count", "busy_count", "circuit_open_count", "network_attempt_count",
    "average_latency_ms", "p95_latency_ms", "fallback_ratio_basis_points",
    "max_observations_per_minute", "retry_storm_detected", "runtime_instance_id",
    "configuration_fingerprint", "started_at_unix_seconds", "identity_stable_across_window",
    "runtime_restart_count", "fault_drill_count", "drill_runtime_unavailable",
    "drill_timeout", "drill_invalid_schema", "drill_unsafe_authority",
    "drill_concurrency_saturation", "drill_circuit_open_recovery", "drill_live_kill_switch",
    "kill_switch_restored", "production_shadow_restored_disabled", "agent_advisory_only",
    "csharp_validation_authority", "allows_execution", "allows_state_write",
    "allows_visible_text", "stores_sensitive_data", "reason_codes"
)
try {
    $artifactExists = Test-Path -LiteralPath $ArtifactPath -PathType Leaf
}
catch {
    throw "artifact_read_failed"
}
if ($artifactExists -eq $false) { throw "artifact_missing" }
try {
    $artifactLines = @(Get-Content -LiteralPath $ArtifactPath)
}
catch {
    throw "artifact_read_failed"
}
$allowedSet = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($key in $allowed) { [void]$allowedSet.Add($key) }
$values = [System.Collections.Generic.Dictionary[string,string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($line in $artifactLines) {
    $separator = $line.IndexOf('=')
    if ($separator -le 0) { throw "invalid_artifact_line" }
    $key = $line.Substring(0, $separator)
    $value = $line.Substring($separator + 1)
    if ($allowedSet.Contains($key) -eq $false) { throw "unknown_artifact_key" }
    if ($values.ContainsKey($key)) { throw "duplicate_artifact_key" }
    $values.Add($key, $value)
}
foreach ($key in $allowed) { if ($values.ContainsKey($key) -eq $false) { throw "artifact_key_missing" } }
if ($values.Count -ne $allowed.Count) { throw "artifact_key_inventory_invalid" }

function Require-Equal([string]$Key, [string]$Expected) {
    if ($values[$Key] -cne $Expected) { throw "artifact_value_invalid" }
}
function Get-CanonicalNumber([string]$Key, [long]$Minimum, [long]$Maximum) {
    if ($values[$Key] -cnotmatch '^(0|[1-9][0-9]*)$') { throw "artifact_number_invalid" }
    $parsed = 0L
    if ([long]::TryParse($values[$Key], [ref]$parsed) -eq $false -or $parsed -lt $Minimum -or $parsed -gt $Maximum) {
        throw "artifact_number_invalid"
    }
    return $parsed
}

Require-Equal "live_canary_closure" "v4.7"
Require-Equal "source_baseline" "v4.6"
Require-Equal "accepted" "true"
Require-Equal "reason_code" "v4_7_live_canary_closure_accepted"
Require-Equal "reason_codes" "v4_7_live_canary_closure_accepted"
$observationCapacity = Get-CanonicalNumber "observation_capacity" 256 256
$observationWindowMinutes = Get-CanonicalNumber "observation_window_minutes" 15 15
$observationCount = Get-CanonicalNumber "observation_count" 20 256
$acceptedCount = Get-CanonicalNumber "accepted_count" 0 $observationCount
$rejectedCount = Get-CanonicalNumber "rejected_count" 0 $observationCount
$fallbackCount = Get-CanonicalNumber "fallback_count" 0 $observationCount
$timeoutCount = Get-CanonicalNumber "timeout_count" 0 $observationCount
$unavailableCount = Get-CanonicalNumber "unavailable_count" 0 $observationCount
$busyCount = Get-CanonicalNumber "busy_count" 0 $observationCount
$circuitOpenCount = Get-CanonicalNumber "circuit_open_count" 0 $observationCount
$networkAttemptCount = Get-CanonicalNumber "network_attempt_count" 0 $observationCount
$averageLatencyMs = Get-CanonicalNumber "average_latency_ms" 0 300000
$p95LatencyMs = Get-CanonicalNumber "p95_latency_ms" 0 2000
$fallbackRatioBasisPoints = Get-CanonicalNumber "fallback_ratio_basis_points" 0 2500
$maxObservationsPerMinute = Get-CanonicalNumber "max_observations_per_minute" 0 $observationCount
$runtimeRestartCount = Get-CanonicalNumber "runtime_restart_count" 0 1
$faultDrillCount = Get-CanonicalNumber "fault_drill_count" 7 7
if (($acceptedCount + $rejectedCount + $fallbackCount + $timeoutCount +
    $unavailableCount + $busyCount + $circuitOpenCount) -ne $observationCount) {
    throw "artifact_count_relation_invalid"
}
if ($acceptedCount -ne $observationCount -or $rejectedCount -ne 0 -or
    $fallbackCount -ne 0 -or $timeoutCount -ne 0 -or $unavailableCount -ne 0 -or
    $busyCount -ne 0 -or $circuitOpenCount -ne 0) {
    throw "artifact_success_window_invalid"
}
if ($fallbackRatioBasisPoints -ne 0) { throw "artifact_fallback_relation_invalid" }
if ($networkAttemptCount -ne $observationCount) { throw "artifact_network_relation_invalid" }
Require-Equal "retry_storm_detected" "false"
Require-Equal "identity_stable_across_window" "true"
foreach ($key in @("drill_runtime_unavailable", "drill_timeout", "drill_invalid_schema",
    "drill_unsafe_authority", "drill_concurrency_saturation", "drill_circuit_open_recovery",
    "drill_live_kill_switch", "kill_switch_restored", "production_shadow_restored_disabled",
    "agent_advisory_only", "csharp_validation_authority")) { Require-Equal $key "true" }
foreach ($key in @("allows_execution", "allows_state_write", "allows_visible_text", "stores_sensitive_data")) {
    Require-Equal $key "false"
}
$uuid = [Guid]::Empty
if ([Guid]::TryParseExact($values["runtime_instance_id"], "D", [ref]$uuid) -eq $false -or
    $uuid.ToString("D") -cne $values["runtime_instance_id"]) { throw "runtime_identity_invalid" }
if ($values["configuration_fingerprint"] -cnotmatch '^[a-f0-9]{64}$') { throw "configuration_fingerprint_invalid" }
$startedAtUnixSeconds = Get-CanonicalNumber "started_at_unix_seconds" 1 253402300799

Write-Output "artifact_verified=true"
