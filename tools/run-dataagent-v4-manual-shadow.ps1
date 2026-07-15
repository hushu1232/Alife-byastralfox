param(
    [string]$BaseUri = "http://127.0.0.1:8765",
    [string]$OutputDirectory = "",
    [int]$TimeoutMs = 2000,
    [string]$ArtifactBridgePath = ""
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

# real_langgraph_manual_shadow_integration=true
# source_baseline=v3.28
# manual_only=true
# operator_started_runtime=true
# loopback_only=true
# agent_advisory_only=true
# harness_execution_authority=true
# csharp_validation_authority=true
# default_result_changed=false
# fallback_required=true
# starts_runtime=false
# installs_dependencies=false
# calls_sidecar=false
# stores_secrets=false
# stores_sql=false
# stores_hidden_context=false

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

    if ($uri.Host.Equals("localhost", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $uri
    }

    $hostForAddress = $uri.Host.Trim('[', ']')
    $address = $null
    if ([System.Net.IPAddress]::TryParse($hostForAddress, [ref]$address) -eq $false -or
        [System.Net.IPAddress]::IsLoopback($address) -eq $false) {
        throw "BaseUri must target a loopback host."
    }

    return $uri
}

function ConvertTo-ManualShadowFailureReason {
    param([object]$Value)

    $fallback = "manual_shadow_failed"
    if ($null -eq $Value) {
        return $fallback
    }

    $text = ([string]$Value) -replace "[\r\n\t]+", " "
    $text = $text.Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $fallback
    }

    $dangerousPattern = "(?i)(\bselect\b|\binsert\b|\bupdate\b|\bdelete\b|\bdrop\b|hidden_context|bearer|token|secret|password|api[_-]?key|authorization|connection\s*string|[A-Za-z]:\\|\\Users\\|/Users/|\.ssh|\.env)"
    if ($text -match $dangerousPattern) {
        return $fallback
    }

    if ($text.Length -gt 80) {
        return $text.Substring(0, 80)
    }

    return $text
}

function ConvertTo-ManualShadowBridgeFallbackReason {
    param([object]$Value)

    if ($null -eq $Value) {
        return "manual_shadow_failed"
    }

    switch ([string]$Value) {
        "manual_shadow_response_rejected" {
            return "manual_shadow_response_rejected"
        }
        default {
            return "manual_shadow_failed"
        }
    }
}

function ConvertTo-WindowsProcessArgument {
    param([string]$Value)

    if ($null -eq $Value) {
        throw "process_argument_missing"
    }

    $quoted = New-Object System.Text.StringBuilder
    [void]$quoted.Append([char]34)
    $backslashCount = 0

    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq [char]92) {
            $backslashCount++
            continue
        }

        if ($character -eq [char]34) {
            [void]$quoted.Append([char]92, ($backslashCount * 2) + 1)
            [void]$quoted.Append([char]34)
            $backslashCount = 0
            continue
        }

        if ($backslashCount -gt 0) {
            [void]$quoted.Append([char]92, $backslashCount)
            $backslashCount = 0
        }

        [void]$quoted.Append($character)
    }

    if ($backslashCount -gt 0) {
        [void]$quoted.Append([char]92, $backslashCount * 2)
    }

    [void]$quoted.Append([char]34)
    return $quoted.ToString()
}

function Invoke-ManualShadowArtifactBridge {
    param(
        [string]$Outcome,
        [string]$ReasonCode,
        [int]$HealthStatusCode,
        [int]$HandshakeStatusCode
    )

    $persisted = $false
    $process = $null
    $discardOutputTask = $null
    $discardErrorTask = $null
    try {
        if (($Outcome -ne "accepted" -and $Outcome -ne "fallback") -or
            ($ReasonCode -ne "manual_shadow_handshake_accepted" -and
             $ReasonCode -ne "manual_shadow_response_rejected" -and
             $ReasonCode -ne "manual_shadow_failed")) {
            throw "artifact_bridge_input_rejected"
        }

        $bridgePath = $ArtifactBridgePath
        if ([string]::IsNullOrWhiteSpace($bridgePath)) {
            $repoRoot = Split-Path -Parent $PSScriptRoot
            $bridgePath = Join-Path `
                $repoRoot `
                "Outputs\\Alife.Tools.DataAgentShadowArtifact\\Alife.Tools.DataAgentShadowArtifact.dll"
        }

        if ((Test-Path -LiteralPath $bridgePath -PathType Leaf) -eq $false) {
            throw "artifact_bridge_missing"
        }

        $bridgeArguments = @(
            $bridgePath,
            "--outcome", $Outcome,
            "--reason-code", $ReasonCode,
            "--health-status", ([int]$HealthStatusCode).ToString([System.Globalization.CultureInfo]::InvariantCulture),
            "--handshake-status", ([int]$HandshakeStatusCode).ToString([System.Globalization.CultureInfo]::InvariantCulture),
            "--context-layers", "3"
        )

        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = "C:\Users\hu shu\.dotnet\dotnet.exe"
        $startInfo.Arguments = (($bridgeArguments | ForEach-Object { ConvertTo-WindowsProcessArgument $_ }) -join " ")
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true

        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $startInfo

        if ($process.Start() -eq $false) {
            throw "artifact_bridge_start_failed"
        }

        $discardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $discardErrorTask = $process.StandardError.ReadToEndAsync()
        if ($process.WaitForExit(2000)) {
            $persisted = $process.ExitCode -eq 0
        }
        else {
            try {
                $process.Kill()
                $null = $process.WaitForExit(1000)
            }
            catch {
            }
        }
    }
    catch {
        $persisted = $false
    }
    finally {
        if ($null -ne $discardOutputTask) {
            $null = $discardOutputTask.Wait(1000)
        }

        if ($null -ne $discardErrorTask) {
            $null = $discardErrorTask.Wait(1000)
        }

        if ($null -ne $process) {
            $process.Dispose()
        }
    }

    Write-Output ("artifact_persisted={0}" -f $persisted.ToString().ToLowerInvariant())
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

function Get-ManualShadowJsonProperty {
    param(
        [pscustomobject]$JsonObject,
        [string[]]$Names
    )

    foreach ($name in $Names) {
        $property = @($JsonObject.PSObject.Properties | Where-Object { $_.Name -ceq $name })
        if ($property.Count -gt 0) {
            return $property[0]
        }
    }

    return $null
}

function Get-ManualShadowJsonProperties {
    param(
        [pscustomobject]$JsonObject,
        [string[]]$Names
    )

    $matches = @()
    foreach ($name in $Names) {
        $matches += @($JsonObject.PSObject.Properties | Where-Object { $_.Name -ceq $name })
    }

    return $matches
}

function Assert-ManualShadowBooleanMarker {
    param(
        [pscustomobject]$JsonObject,
        [string[]]$Names,
        [bool]$Expected,
        [bool]$Required = $true
    )

    $properties = @(Get-ManualShadowJsonProperties -JsonObject $JsonObject -Names $Names)
    if ($properties.Count -eq 0) {
        if ($Required) {
            throw "manual_shadow_response_rejected"
        }

        return
    }

    foreach ($property in $properties) {
        if (($property.Value -is [bool]) -eq $false) {
            throw "manual_shadow_response_rejected"
        }

        if ([bool]$property.Value -ne $Expected) {
            throw "manual_shadow_response_rejected"
        }
    }
}

function Assert-ManualShadowForbiddenAuthorityClaims {
    param([pscustomobject]$JsonObject)

    $properties = @(Get-ManualShadowJsonProperties `
        -JsonObject $JsonObject `
        -Names @("forbidden_authority_claims", "ForbiddenAuthorityClaims"))

    if ($properties.Count -eq 0) {
        throw "manual_shadow_response_rejected"
    }

    foreach ($property in $properties) {
        if ($null -eq $property.Value) {
            throw "manual_shadow_response_rejected"
        }

        if ($property.Value -is [string] -or
            $property.Value -is [pscustomobject] -or
            $property.Value -is [System.Collections.IDictionary] -or
            ($property.Value -is [System.Collections.IEnumerable]) -eq $false) {
            throw "manual_shadow_response_rejected"
        }

        $enumerator = ([System.Collections.IEnumerable]$property.Value).GetEnumerator()
        try {
            if ($enumerator.MoveNext()) {
                throw "manual_shadow_response_rejected"
            }
        }
        finally {
            if ($enumerator -is [System.IDisposable]) {
                $enumerator.Dispose()
            }
        }
    }
}

function Assert-ManualShadowHandshakeResponse {
    param([object]$Response)

    if ($null -eq $Response) {
        throw "manual_shadow_response_rejected"
    }

    $contentProperties = @($Response.PSObject.Properties | Where-Object { $_.Name -ceq "Content" })
    if ($contentProperties.Count -ne 1) {
        throw "manual_shadow_response_rejected"
    }

    if (($contentProperties[0].Value -is [string]) -eq $false) {
        throw "manual_shadow_response_rejected"
    }

    $content = $contentProperties[0].Value
    if ([string]::IsNullOrWhiteSpace($content)) {
        throw "manual_shadow_response_rejected"
    }

    try {
        $json = $content | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "manual_shadow_response_rejected"
    }

    if ($null -eq $json -or $json -is [array] -or ($json -is [pscustomobject]) -eq $false) {
        throw "manual_shadow_response_rejected"
    }

    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("accepted", "Accepted") -Expected $true
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("agent_advisory_only", "AgentAdvisoryOnly") -Expected $true
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("harness_execution_authority", "HarnessExecutionAuthority") -Expected $true
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("csharp_validation_authority", "CSharpValidationAuthority") -Expected $true
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("default_result_changed", "DefaultResultChanged") -Expected $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("fallback_required", "FallbackRequired") -Expected $true
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("starts_runtime", "StartsRuntime") -Expected $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("installs_dependencies", "InstallsDependencies") -Expected $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("calls_sidecar", "CallsSidecar") -Expected $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("stores_secrets", "StoresSecrets") -Expected $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("stores_sql", "StoresSql") -Expected $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("stores_hidden_context", "StoresHiddenContext") -Expected $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("replay_diff_gate_passed", "ReplayDiffGatePassed") -Expected $true
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("forbidden_authority_claimed", "ForbiddenAuthorityClaimed") -Expected $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("requests_visible_text", "RequestsVisibleText") -Expected $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("requests_checkpoint_write", "RequestsCheckpointWrite") -Expected $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("requests_sql_authority", "RequestsSqlAuthority") -Expected $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("requests_state_write", "RequestsStateWrite") -Expected $false

    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("no_sql_authority", "NoSqlAuthority") -Expected $true -Required $false
    Assert-ManualShadowBooleanMarker -JsonObject $json -Names @("requests_execution", "RequestsExecution") -Expected $false -Required $false
    Assert-ManualShadowForbiddenAuthorityClaims -JsonObject $json

    return $true
}

function Write-ManualShadowArtifact {
    param(
        [string]$OutputDirectory,
        [int]$HealthStatusCode,
        [int]$HandshakeStatusCode,
        [bool]$HandshakeValidated = $false
    )

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        throw "OutputDirectory is required."
    }

    $artifact = [ordered]@{
        real_langgraph_manual_shadow_integration = $true
        manual_only = $true
        operator_started_runtime = $true
        loopback_only = $true
        starts_runtime = $false
        installs_dependencies = $false
        calls_sidecar = $false
        default_result_changed = $false
        handshake_validated = $HandshakeValidated
        health_status_code = [int]$HealthStatusCode
        handshake_status_code = [int]$HandshakeStatusCode
    }

    $artifactFileName = "dataagent-v4.0-real-langgraph-manual-shadow.json"
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $artifactPath = Join-Path $OutputDirectory $artifactFileName
    $artifact | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $artifactPath -Encoding UTF8
    Write-Output "artifact_written=true"
    Write-Output ("artifact_file={0}" -f $artifactFileName)
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
        ContextBudget = [ordered]@{
            MaxEnvelopeChars = 1200
            MaxLayerChars = 400
            RequiredLayerNames = @(
                "layer_1_route",
                "layer_2_evidence",
                "layer_3_excerpt"
            )
        }
        ContextLayers = @(
            [ordered]@{
                Name = "layer_1_route"
                Text = "fixture=v4.1-manual-shadow;route=allowed;node=manual_shadow"
            },
            [ordered]@{
                Name = "layer_2_evidence"
                Text = "reason_code=manual_shadow_review;evidence_ref=v3.28-final-readiness-freeze"
            },
            [ordered]@{
                Name = "layer_3_excerpt"
                Text = "bounded_failure_excerpt=operator_review_required"
            }
        )
        TraceBudgetChars = 1200
        ProgressBudget = 8
    }
}

Write-Output "DataAgent V4.0 manual LangGraph shadow"

$healthStatusCode = 0
$handshakeStatusCode = 0

try {
    if ($TimeoutMs -le 0) {
        throw "TimeoutMs must be greater than zero."
    }

    $base = Assert-LoopbackBaseUri $BaseUri
    $timeoutSeconds = [Math]::Max(1, [int][Math]::Ceiling($TimeoutMs / 1000.0))
    $request = New-V40HandshakeRequest

    $healthResponse = Invoke-JsonRequest -Method "GET" -Uri (Join-SidecarUri $base "/health") -TimeoutSeconds $timeoutSeconds
    $healthStatusCode = [int]$healthResponse.StatusCode
    $handshakeResponse = Invoke-JsonRequest -Method "POST" -Uri (Join-SidecarUri $base "/handshake") -Body $request -TimeoutSeconds $timeoutSeconds
    $handshakeStatusCode = [int]$handshakeResponse.StatusCode
    $handshakeValidated = Assert-ManualShadowHandshakeResponse $handshakeResponse

    if ([string]::IsNullOrWhiteSpace($OutputDirectory) -eq $false) {
        Write-ManualShadowArtifact `
            -OutputDirectory $OutputDirectory `
            -HealthStatusCode ([int]$healthResponse.StatusCode) `
            -HandshakeStatusCode ([int]$handshakeResponse.StatusCode) `
            -HandshakeValidated $handshakeValidated
    }

    Invoke-ManualShadowArtifactBridge `
        -Outcome "accepted" `
        -ReasonCode "manual_shadow_handshake_accepted" `
        -HealthStatusCode $healthStatusCode `
        -HandshakeStatusCode $handshakeStatusCode

    Write-Output "handshake_validated=true"
    Write-Output "PASS manual_shadow"
    exit 0
}
catch {
    $reason = ConvertTo-ManualShadowFailureReason $_.Exception.Message
    $bridgeReason = ConvertTo-ManualShadowBridgeFallbackReason $_.Exception.Message
    Invoke-ManualShadowArtifactBridge `
        -Outcome "fallback" `
        -ReasonCode $bridgeReason `
        -HealthStatusCode $healthStatusCode `
        -HandshakeStatusCode $handshakeStatusCode
    Write-Output ("FALLBACK manual_shadow {0}" -f $reason)
    exit 1
}
