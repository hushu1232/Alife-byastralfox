param(
    [string]$BaseUri = "http://127.0.0.1:8765",
    [int]$TimeoutMs = 2000
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

# DataAgent graph sidecar live smoke
# V3.4 smoke boundary markers:
# manual_only=true
# starts_runtime=false
# installs_dependencies=false
# loopback_only=true
# default_tests_live_runtime=false

$script:PassedChecks = 0
$script:FailedChecks = 0

function Write-Pass {
    param([string]$Name, [string]$Detail = "")

    $script:PassedChecks++
    if ([string]::IsNullOrWhiteSpace($Detail)) {
        Write-Output ("PASS {0}" -f $Name)
        return
    }

    Write-Output ("PASS {0} {1}" -f $Name, $Detail)
}

function Write-Fail {
    param([string]$Name, [string]$Detail)

    $script:FailedChecks++
    Write-Output ("FAIL {0} {1}" -f $Name, $Detail)
}

function Complete-Smoke {
    Write-Output ("Summary: {0} passed, {1} failed" -f $script:PassedChecks, $script:FailedChecks)
    if ($script:FailedChecks -gt 0) {
        exit 1
    }

    exit 0
}

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

    $allowedHosts = @("127.0.0.1", "localhost")
    if ($allowedHosts -notcontains $uri.Host) {
        throw "BaseUri must target loopback host 127.0.0.1 or localhost."
    }

    return $uri
}

function New-SmokeHandshakeRequest {
    [ordered]@{
        RequestId = "graph-sidecar-smoke-session-1-turn-1"
        SessionId = "graph-sidecar-smoke-session-1"
        TurnId = "turn-1"
        CallerId = "owner"
        GoalOrQuestion = "Which readiness gates should the dev sidecar suggest?"
        ScenarioContextSummary = "scenario_context=dev_sidecar_smoke"
        RouteScope = "route_present=true;route_allows_query=true;route_reason_code=route_allowed"
        QueryConstraints = "status=Active;executed_sql=false;terminal=false"
        NodeManifests = @(
            [ordered]@{
                NodeName = "scenario_knowledge"
                Purpose = "Read scenario context"
                AllowedToolNames = @("dataagent.scenario_context.read")
                DeniedCapabilityMarkers = @("sql.execute", "qchat", "browser", "file", "checkpoint.write")
                InputShape = "scenario_context"
                OutputShape = "scenario_summary"
                BusinessTerms = @("readiness", "scenario")
                SafetyNotes = "No SQL or runtime side effects"
            },
            [ordered]@{
                NodeName = "query_planner"
                Purpose = "Suggest read-only query plan"
                AllowedToolNames = @("dataagent.query_plan.propose")
                DeniedCapabilityMarkers = @("sql.execute", "qchat", "browser", "file", "checkpoint.write")
                InputShape = "question"
                OutputShape = "query_plan"
                BusinessTerms = @("planner", "readiness")
                SafetyNotes = "No SQL execution authority"
            },
            [ordered]@{
                NodeName = "diagnostics_router"
                Purpose = "Suggest diagnostics route"
                AllowedToolNames = @("dataagent.diagnostics.progress.read")
                DeniedCapabilityMarkers = @("sql.execute", "qchat", "browser", "file", "checkpoint.write")
                InputShape = "diagnostics_request"
                OutputShape = "diagnostics_hint"
                BusinessTerms = @("diagnostics", "progress")
                SafetyNotes = "No owner diagnostics publishing authority"
            }
        )
        NoSqlAuthority = $true
        ReadOnly = $true
        FallbackAvailable = $true
        TraceBudgetChars = 1800
        ProgressBudget = 16
    }
}

function Test-ForbiddenToolName {
    param([string]$ToolName)

    if ([string]::IsNullOrWhiteSpace($ToolName)) {
        return $false
    }

    $forbiddenMarkers = @(
        "qchat",
        "qq",
        "browser",
        "file",
        "rag.manage",
        "checkpoint.write",
        "dataagent.query.execute_readonly"
    )

    foreach ($marker in $forbiddenMarkers) {
        if ($ToolName.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Test-ReservedProgressFacts {
    param($Facts)

    if ($null -eq $Facts) {
        return $false
    }

    $reserved = @("source", "node", "request_id")
    foreach ($key in $Facts.PSObject.Properties.Name) {
        foreach ($reservedKey in $reserved) {
            if ([string]::Equals($key, $reservedKey, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }

    return $false
}

function Assert-PropertyPresent {
    param($Object, [string]$Name)

    if ($null -eq $Object -or $Object.PSObject.Properties.Name -notcontains $Name) {
        throw ("missing property {0}" -f $Name)
    }
}

function Assert-RequiredString {
    param($Object, [string]$Name)

    Assert-PropertyPresent $Object $Name
    $value = $Object.$Name
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        throw ("{0} must be a non-empty string." -f $Name)
    }

    return $value
}

function Assert-BooleanEquals {
    param($Object, [string]$Name, [bool]$Expected)

    Assert-PropertyPresent $Object $Name
    $value = $Object.$Name
    if ($value -isnot [bool]) {
        throw ("{0} must be a JSON boolean." -f $Name)
    }

    if ($value -ne $Expected) {
        throw ("{0} must be {1}." -f $Name, $Expected.ToString().ToLowerInvariant())
    }
}

function Assert-NonEmptyList {
    param($Object, [string]$Name)

    Assert-PropertyPresent $Object $Name
    $value = $Object.$Name
    if ($null -eq $value) {
        throw ("{0} must be non-null." -f $Name)
    }

    $items = @($value)
    if ($items.Count -le 0) {
        throw ("{0} must be non-empty." -f $Name)
    }

    return $items
}

function Test-HandshakeResponse {
    param($Response, [string]$ExpectedRequestId)

    $requestId = Assert-RequiredString $Response "RequestId"
    if ($requestId -ne $ExpectedRequestId) {
        throw ("RequestId mismatch: expected {0}, got {1}" -f $ExpectedRequestId, $requestId)
    }

    Assert-BooleanEquals $Response "Accepted" $true
    Assert-BooleanEquals $Response "NoSqlAuthority" $true
    Assert-BooleanEquals $Response "ReadOnly" $true
    Assert-BooleanEquals $Response "FallbackRequired" $false
    Assert-BooleanEquals $Response "RequestsCheckpointMutation" $false
    Assert-BooleanEquals $Response "RequestsVisibleText" $false

    $selectedNodes = Assert-NonEmptyList $Response "SelectedNodes"
    $nodeProgress = Assert-NonEmptyList $Response "NodeProgress"
    $requestedToolNames = Assert-NonEmptyList $Response "RequestedToolNames"

    foreach ($toolName in $requestedToolNames) {
        if ($toolName -isnot [string] -or [string]::IsNullOrWhiteSpace($toolName)) {
            throw "RequestedToolNames entries must be non-empty strings."
        }

        if (Test-ForbiddenToolName ([string]$toolName)) {
            throw ("RequestedToolNames contains forbidden authority marker: {0}" -f $toolName)
        }
    }

    foreach ($progress in $nodeProgress) {
        Assert-RequiredString $progress "NodeName" | Out-Null
        Assert-RequiredString $progress "Status" | Out-Null
        Assert-RequiredString $progress "ReasonCode" | Out-Null
        if (Test-ReservedProgressFacts $progress.Facts) {
            throw "NodeProgress Facts must not include reserved C# stamped keys source, node, or request_id."
        }
    }
}

function Join-SidecarUri {
    param([System.Uri]$Base, [string]$Path)

    return (New-Object System.Uri($Base, $Path))
}

function Invoke-SidecarRequest {
    param(
        [string]$Method,
        [System.Uri]$Uri,
        [object]$Body = $null,
        [int]$TimeoutSeconds
    )

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

function ConvertFrom-StrictJson {
    param([string]$Json)

    if ([string]::IsNullOrWhiteSpace($Json)) {
        throw "JSON payload is empty."
    }

    $Json | ConvertFrom-Json
}

function Test-NdjsonStream {
    param([string]$Body, [string]$ExpectedRequestId)

    $lines = @($Body -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -eq 0) {
        throw "NDJSON response had no events."
    }

    $progressCount = 0
    $finalCount = 0
    $seenFinal = $false

    foreach ($line in $lines) {
        $event = ConvertFrom-StrictJson $line
        $kind = Assert-RequiredString $event "Kind"

        if ($kind -ne "Progress" -and $kind -ne "FinalResponse") {
            throw ("invalid NDJSON event Kind: {0}" -f $kind)
        }

        if ($seenFinal) {
            throw "No event may appear after FinalResponse."
        }

        if ($kind -eq "Progress") {
            if ($event.PSObject.Properties.Name -notcontains "Progress" -or $event.PSObject.Properties.Name -contains "Response") {
                throw "Progress event must contain Progress and must not contain Response."
            }

            Assert-RequiredString $event.Progress "NodeName" | Out-Null
            $status = Assert-RequiredString $event.Progress "Status"
            Assert-RequiredString $event.Progress "ReasonCode" | Out-Null
            $allowedStatuses = @("Started", "Completed", "Skipped", "Rejected", "Failed")
            if ($allowedStatuses -notcontains $status) {
                throw ("Progress Status must be one of {0}." -f ($allowedStatuses -join ", "))
            }

            if (Test-ReservedProgressFacts $event.Progress.Facts) {
                throw "Stream progress Facts must not include reserved C# stamped keys source, node, or request_id."
            }

            $progressCount++
            continue
        }

        if ($event.PSObject.Properties.Name -notcontains "Response" -or $event.PSObject.Properties.Name -contains "Progress") {
            throw "FinalResponse event must contain Response and must not contain Progress."
        }

        $seenFinal = $true
        $finalCount++
        Test-HandshakeResponse $event.Response $ExpectedRequestId
    }

    if ($progressCount -le 0) {
        throw "Expected at least one Progress event."
    }

    if ($finalCount -ne 1) {
        throw ("Expected exactly one FinalResponse event, got {0}." -f $finalCount)
    }

    [pscustomobject]@{
        ProgressCount = $progressCount
        FinalResponse = $true
    }
}

Write-Output "DataAgent graph sidecar live smoke"

try {
    if ($TimeoutMs -le 0) {
        throw "TimeoutMs must be greater than zero."
    }

    $base = Assert-LoopbackBaseUri $BaseUri
    $timeoutSeconds = [Math]::Max(1, [int][Math]::Ceiling($TimeoutMs / 1000.0))
    $request = New-SmokeHandshakeRequest
    Write-Output ("BaseUri: {0}" -f $base.AbsoluteUri.TrimEnd('/'))

    try {
        $healthResponse = Invoke-SidecarRequest -Method "GET" -Uri (Join-SidecarUri $base "/health") -TimeoutSeconds $timeoutSeconds
        $health = ConvertFrom-StrictJson $healthResponse.Content
        if ($health.status -ne "ok" -or $health.runtime -ne "dev_sidecar") {
            throw ("expected status=ok runtime=dev_sidecar, got status={0} runtime={1}" -f $health.status, $health.runtime)
        }

        Write-Pass "health" "status=ok runtime=dev_sidecar"
    }
    catch {
        Write-Fail "health" ("{0}. Start the sidecar manually using tools/dataagent-graph-sidecar/README.md." -f $_.Exception.Message)
        Complete-Smoke
    }

    try {
        $handshakeResponse = Invoke-SidecarRequest -Method "POST" -Uri (Join-SidecarUri $base "/handshake") -Body $request -TimeoutSeconds $timeoutSeconds
        $handshake = ConvertFrom-StrictJson $handshakeResponse.Content
        Test-HandshakeResponse $handshake $request.RequestId
        Write-Pass "handshake" ("accepted=true selected_nodes={0} progress={1}" -f @($handshake.SelectedNodes).Count, @($handshake.NodeProgress).Count)
    }
    catch {
        Write-Fail "handshake" $_.Exception.Message
    }

    try {
        $streamResponse = Invoke-SidecarRequest -Method "POST" -Uri (Join-SidecarUri $base "/handshake-stream") -Body $request -TimeoutSeconds $timeoutSeconds
        $contentType = [string]$streamResponse.Headers["Content-Type"]
        $sseMediaType = ("text/" + "event-stream")
        if ($contentType.IndexOf($sseMediaType, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "SSE media type is deferred and must not be returned by V3.4 smoke."
        }

        if ($contentType.IndexOf("application/x-ndjson", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw ("expected application/x-ndjson content type, got {0}" -f $contentType)
        }

        $stream = Test-NdjsonStream $streamResponse.Content $request.RequestId
        Write-Pass "handshake-stream" ("progress={0} final_response={1}" -f $stream.ProgressCount, $stream.FinalResponse.ToString().ToLowerInvariant())
    }
    catch {
        Write-Fail "handshake-stream" $_.Exception.Message
    }
}
catch {
    Write-Fail "setup" $_.Exception.Message
}

Complete-Smoke
