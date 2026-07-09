param(
    [string]$BaseUri = "http://127.0.0.1:8765",
    [string]$OutputDirectory = "",
    [int]$TimeoutMs = 2000
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

# real_langgraph_manual_shadow_integration=true
# manual_only=true
# operator_started_runtime=true
# loopback_only=true
# starts_runtime=false
# installs_dependencies=false

function Assert-LoopbackBaseUri {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "BaseUri is required."
    }

    $uri = $null
    if ([System.Uri]::TryCreate($Value.TrimEnd('/'), [System.UriKind]::Absolute, [ref]$uri) -eq $false) {
        throw "BaseUri must be an absolute URI."
    }

    if ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https") {
        throw "BaseUri must use http or https."
    }

    if ([string]::IsNullOrEmpty($uri.UserInfo) -eq $false) {
        throw "BaseUri must not include user information."
    }

    $allowedHosts = @("127.0.0.1", "localhost", "::1")
    if ($allowedHosts -notcontains $uri.Host) {
        throw "BaseUri must target loopback host 127.0.0.1, localhost, or ::1."
    }

    return $uri
}

function Join-SidecarUri {
    param(
        [System.Uri]$Base,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Path is required."
    }

    return (New-Object System.Uri($Base, $Path))
}

function Invoke-JsonRequest {
    param(
        [string]$Method,
        [System.Uri]$Uri,
        [object]$Body = $null,
        [int]$TimeoutSeconds
    )

    if ($TimeoutSeconds -le 0) {
        throw "TimeoutSeconds must be greater than zero."
    }

    $parameters = @{
        Method = $Method
        Uri = $Uri
        TimeoutSec = $TimeoutSeconds
        UseBasicParsing = $true
    }

    if ($null -ne $Body) {
        $parameters.Body = ($Body | ConvertTo-Json -Depth 16 -Compress)
        $parameters.ContentType = "application/json"
    }

    Invoke-WebRequest @parameters
}

function New-V40HandshakeRequest {
    [ordered]@{
        RequestId = "v4-manual-shadow-operator-run"
        SessionId = "v4-manual-shadow"
        TurnId = "manual-shadow-1"
        CallerId = "operator"
        GoalOrQuestion = "Summarize replay evidence for operator review."
        ScenarioContextSummary = "source_baseline=v3.28;manual_only=true"
        RouteScope = "route_present=true;route_allows_query=true;route_reason_code=route_allowed"
        QueryConstraints = "default_result_changed=false;execute_sql=false"
        NodeManifests = @(
            [ordered]@{
                NodeName = "diagnostics_router"
                Purpose = "Summarize replay evidence"
                AllowedToolNames = @("dataagent.diagnostics.progress.read")
                DeniedCapabilityMarkers = @("sql.execute", "checkpoint.write", "qchat.visible_text", "tool.execute")
                InputShape = "replay_evidence"
                OutputShape = "advisory_summary"
                BusinessTerms = @("replay", "diagnostics", "operator")
                SafetyNotes = "No execution or persistence authority"
            }
        )
        NoSqlAuthority = $true
        ReadOnly = $true
        FallbackAvailable = $true
        TraceBudgetChars = 1200
        ProgressBudget = 8
    }
}

Write-Output "DataAgent V4.0 manual LangGraph shadow"

try {
    if ($TimeoutMs -le 0) {
        throw "TimeoutMs must be greater than zero."
    }

    $base = Assert-LoopbackBaseUri $BaseUri
    $timeoutSeconds = [Math]::Max(1, [int][Math]::Ceiling($TimeoutMs / 1000.0))
    $request = New-V40HandshakeRequest

    $healthResponse = Invoke-JsonRequest -Method "GET" -Uri (Join-SidecarUri $base "/health") -TimeoutSeconds $timeoutSeconds
    $handshakeResponse = Invoke-JsonRequest -Method "POST" -Uri (Join-SidecarUri $base "/handshake") -Body $request -TimeoutSeconds $timeoutSeconds

    $artifact = [ordered]@{
        real_langgraph_manual_shadow_integration = $true
        manual_only = $true
        operator_started_runtime = $true
        loopback_only = $true
        starts_runtime = $false
        installs_dependencies = $false
        default_result_changed = $false
        health_status_code = [int]$healthResponse.StatusCode
        handshake_status_code = [int]$handshakeResponse.StatusCode
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory) -eq $false) {
        New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
        $artifactPath = Join-Path $OutputDirectory "dataagent-v4.0-manual-langgraph-shadow.json"
        $artifact | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $artifactPath -Encoding UTF8
        Write-Output ("artifact={0}" -f $artifactPath)
    }

    Write-Output "PASS manual_shadow"
    exit 0
}
catch {
    Write-Output ("FALLBACK manual_shadow {0}" -f $_.Exception.Message)
    exit 1
}
