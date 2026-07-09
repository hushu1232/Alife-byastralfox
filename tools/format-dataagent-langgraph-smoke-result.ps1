param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

# artifact_formatter=true
# manual_only=true
# stores_secrets=false
# stores_sql=false
# stores_hidden_context=false
# sanitizes_unsafe_text=true

$script:UnsafeTextRedacted = $false

function Protect-MachineToken {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    $text = [string]$Value
    if ($text -match '^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$') {
        return $text
    }

    $script:UnsafeTextRedacted = $true
    return "redacted"
}

function Protect-SmokeText {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    $text = [string]$Value
    $unsafePattern = '(?i)```sql|\b(select|insert|update|delete|drop|alter|truncate|merge|grant|revoke|pragma|execute|call)\b|hidden_context|bearer|token|secret|api[_-]?key|qchat|visible text'
    if ($text -match $unsafePattern) {
        $script:UnsafeTextRedacted = $true
        return "redacted"
    }

    if ($text.Length -gt 256) {
        return $text.Substring(0, 256)
    }

    return $text
}

function Get-BoolProperty {
    param(
        [object]$Source,
        [string]$Name
    )

    if ($null -eq $Source.PSObject.Properties[$Name]) {
        return $false
    }

    return [bool]$Source.PSObject.Properties[$Name].Value
}

if ((Test-Path -LiteralPath $InputPath) -eq $false) {
    throw "InputPath does not exist."
}

$response = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
$artifact = [ordered]@{
    artifact_formatter = $true
    manual_only = $true
    request_id = Protect-MachineToken $response.RequestId
    accepted = Get-BoolProperty $response "Accepted"
    reason_code = Protect-MachineToken $response.ReasonCode
    no_sql_authority = Get-BoolProperty $response "NoSqlAuthority"
    read_only = Get-BoolProperty $response "ReadOnly"
    fallback_required = Get-BoolProperty $response "FallbackRequired"
    requests_checkpoint_mutation = Get-BoolProperty $response "RequestsCheckpointMutation"
    requests_visible_text = Get-BoolProperty $response "RequestsVisibleText"
    trace_summary = Protect-SmokeText $response.TraceSummary
    context_contribution = Protect-SmokeText $response.ContextContribution
    unsafe_text_redacted = $script:UnsafeTextRedacted
}

$outputDirectory = Split-Path -Parent $OutputPath
if ([string]::IsNullOrWhiteSpace($outputDirectory) -eq $false) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$json = ($artifact | ConvertTo-Json -Depth 4).Replace(":  ", ": ")
Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8
Write-Output "PASS smoke result artifact formatted"
