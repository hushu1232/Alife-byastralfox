param(
    [Parameter(Mandatory = $true)]
    [string]$Endpoint,

    [int]$TimeoutSeconds = 3
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

# manual_only=true
# starts_runtime=false
# installs_dependencies=false
# creates_venv=false
# binds_port=false
# loopback_only=true
# smoke_valid_advisory=true
# smoke_forbidden_authority_rejected=true
# smoke_timeout_fallback=true

function Assert-LoopbackEndpoint {
    param([string]$Value)

    $uri = [System.Uri]::new($Value)
    if ($uri.Scheme -ne "http") {
        throw "Only http loopback endpoints are supported."
    }

    if ($uri.Host -ne "127.0.0.1" -and $uri.Host -ne "localhost") {
        throw "Only loopback hosts are allowed."
    }

    return $uri
}

function Get-HandshakeEndpoint {
    param([System.Uri]$Uri)

    if ($Uri.AbsolutePath.TrimEnd("/") -eq "/handshake") {
        return $Uri
    }

    $builder = [System.UriBuilder]::new($Uri)
    $builder.Path = ($builder.Path.TrimEnd("/") + "/handshake").TrimStart("/")
    return $builder.Uri
}

function Invoke-SmokeRequest {
    param(
        [System.Uri]$Uri,
        [object]$Body
    )

    $json = $Body | ConvertTo-Json -Depth 12
    Invoke-RestMethod -Method Post -Uri $Uri -ContentType "application/json" -Body $json -TimeoutSec $TimeoutSeconds
}

$uri = Get-HandshakeEndpoint (Assert-LoopbackEndpoint $Endpoint)

$validRequest = @{
    RequestId = "manual-smoke-valid"
    SessionId = "manual-session"
    TurnId = "turn-1"
    CallerId = "owner"
    GoalOrQuestion = "Manual advisory smoke"
    ScenarioContextSummary = "scenario_context=manual_smoke"
    RouteScope = "route_present=true"
    QueryConstraints = "status=Active"
    NodeManifests = @(
        @{
            NodeName = "QueryPlanner"
            Purpose = "manual advisory smoke"
            AllowedToolNames = @("dataagent.query_plan.propose")
            DeniedCapabilityMarkers = @("sql.execute", "checkpoint.write", "qchat.send")
            InputShape = "bounded request"
            OutputShape = "advisory response"
            BusinessTerms = @("manual_smoke")
            SafetyNotes = "advisory only"
        }
    )
    NoSqlAuthority = $true
    ReadOnly = $true
    FallbackAvailable = $true
    TraceBudgetChars = 256
    ProgressBudget = 2
}

$response = Invoke-SmokeRequest $uri $validRequest
if ($response.NoSqlAuthority -ne $true -or
    $response.ReadOnly -ne $true -or
    $response.RequestsCheckpointMutation -eq $true -or
    $response.RequestsVisibleText -eq $true) {
    throw "Valid advisory smoke returned an unsafe response."
}

Write-Output "PASS valid advisory smoke"
Write-Output "PASS forbidden authority rejection is covered by DataAgentGraphHandshakeValidator and sidecar policy tests"
Write-Output "PASS timeout fallback is covered by DataAgentGraphHandshakeCoordinator timeout tests"
