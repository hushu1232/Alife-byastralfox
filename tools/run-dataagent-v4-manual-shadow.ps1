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
                $null = $process.WaitForExit(250)
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
            try {
                $null = $discardOutputTask.Wait(250)
            }
            catch {
            }
        }

        if ($null -ne $discardErrorTask) {
            try {
                $null = $discardErrorTask.Wait(250)
            }
            catch {
            }
        }

        if ($null -ne $process) {
            try {
                $process.Dispose()
            }
            catch {
            }
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

function ConvertTo-ManualShadowStrictJsonObject {
    param(
        [object]$Response,
        [int]$MaximumContentChars
    )

    if ($null -eq $Response) {
        throw "manual_shadow_response_rejected"
    }

    $contentProperties = @($Response.PSObject.Properties | Where-Object { $_.Name -ceq "Content" })
    if ($contentProperties.Count -ne 1 -or ($contentProperties[0].Value -is [string]) -eq $false) {
        throw "manual_shadow_response_rejected"
    }

    $content = $contentProperties[0].Value
    if ([string]::IsNullOrWhiteSpace($content) -or $content.Length -gt $MaximumContentChars) {
        throw "manual_shadow_response_rejected"
    }

    try {
        Add-Type -AssemblyName System.Web.Extensions -ErrorAction Stop
        $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
        $serializer.MaxJsonLength = $MaximumContentChars
        $json = $serializer.DeserializeObject($content)
    }
    catch {
        throw "manual_shadow_response_rejected"
    }

    if ($null -eq $json -or ($json -is [System.Collections.IDictionary]) -eq $false) {
        throw "manual_shadow_response_rejected"
    }

    return $json
}

function Assert-ManualShadowExactFields {
    param(
        [System.Collections.IDictionary]$JsonObject,
        [string[]]$ExpectedFields
    )

    $actualFields = @($JsonObject.Keys)
    if ($actualFields.Count -ne $ExpectedFields.Count) {
        throw "manual_shadow_response_rejected"
    }

    foreach ($expectedField in $ExpectedFields) {
        if (@($JsonObject.Keys | Where-Object { $_ -ceq $expectedField }).Count -ne 1) {
            throw "manual_shadow_response_rejected"
        }
    }
}

function Get-ManualShadowExactValue {
    param(
        [System.Collections.IDictionary]$JsonObject,
        [string]$Name
    )

    if (@($JsonObject.Keys | Where-Object { $_ -ceq $Name }).Count -ne 1) {
        throw "manual_shadow_response_rejected"
    }

    return ,$JsonObject[$Name]
}

function Assert-ManualShadowBooleanValue {
    param(
        [System.Collections.IDictionary]$JsonObject,
        [string]$Name,
        [bool]$Expected
    )

    $value = Get-ManualShadowExactValue -JsonObject $JsonObject -Name $Name
    if (($value -is [bool]) -eq $false -or [bool]$value -ne $Expected) {
        throw "manual_shadow_response_rejected"
    }
}

function Assert-ManualShadowStringValue {
    param(
        [System.Collections.IDictionary]$JsonObject,
        [string]$Name,
        [string]$Expected,
        [int]$MaximumLength = 256
    )

    $value = Get-ManualShadowExactValue -JsonObject $JsonObject -Name $Name
    if (($value -is [string]) -eq $false -or [string]::IsNullOrWhiteSpace($value) -or
        $value.Length -gt $MaximumLength -or $value -cne $Expected) {
        throw "manual_shadow_response_rejected"
    }
}

function Get-ManualShadowBoundedArray {
    param(
        [System.Collections.IDictionary]$JsonObject,
        [string]$Name,
        [int]$MinimumCount,
        [int]$MaximumCount
    )

    $value = Get-ManualShadowExactValue -JsonObject $JsonObject -Name $Name
    if ($null -eq $value -or $value -is [string] -or ($value -is [System.Collections.IEnumerable]) -eq $false) {
        throw "manual_shadow_response_rejected"
    }

    $items = @($value)
    if ($items.Count -lt $MinimumCount -or $items.Count -gt $MaximumCount) {
        throw "manual_shadow_response_rejected"
    }

    return $items
}

function Assert-ManualShadowV47Progress {
    param([object[]]$ProgressItems)

    foreach ($progress in $ProgressItems) {
        if ($null -eq $progress -or ($progress -is [System.Collections.IDictionary]) -eq $false) {
            throw "manual_shadow_response_rejected"
        }

        Assert-ManualShadowExactFields -JsonObject $progress -ExpectedFields @("NodeName", "Status", "ReasonCode", "Message", "Facts")
        Assert-ManualShadowStringValue -JsonObject $progress -Name "NodeName" -Expected "diagnostics_router" -MaximumLength 64
        Assert-ManualShadowStringValue -JsonObject $progress -Name "Status" -Expected "Completed" -MaximumLength 32
        Assert-ManualShadowStringValue -JsonObject $progress -Name "ReasonCode" -Expected "advisory_only" -MaximumLength 64

        $message = Get-ManualShadowExactValue -JsonObject $progress -Name "Message"
        if (($message -is [string]) -eq $false -or [string]::IsNullOrWhiteSpace($message) -or $message.Length -gt 512) {
            throw "manual_shadow_response_rejected"
        }

        $facts = Get-ManualShadowExactValue -JsonObject $progress -Name "Facts"
        if ($null -eq $facts -or ($facts -is [System.Collections.IDictionary]) -eq $false -or $facts.Count -gt 8) {
            throw "manual_shadow_response_rejected"
        }

        foreach ($factKey in $facts.Keys) {
            $factValue = $facts[$factKey]
            if ([string]::IsNullOrWhiteSpace([string]$factKey) -or $factKey.Length -gt 64 -or
                ($factValue -is [string]) -eq $false -or $factValue.Length -gt 256) {
                throw "manual_shadow_response_rejected"
            }
        }
    }
}

function Assert-ManualShadowV47HandshakeResponse {
    param([object]$Response)

    $json = ConvertTo-ManualShadowStrictJsonObject -Response $Response -MaximumContentChars 16384
    Assert-ManualShadowExactFields -JsonObject $json -ExpectedFields @(
        "RequestId", "Accepted", "ReasonCode", "SelectedNodes", "NodeProgress",
        "TraceSummary", "ContextContribution", "FallbackRequired", "NoSqlAuthority",
        "ReadOnly", "RequestedToolNames", "RequestsCheckpointMutation", "RequestsVisibleText"
    )

    Assert-ManualShadowStringValue -JsonObject $json -Name "RequestId" -Expected "v4-manual-shadow-operator-run" -MaximumLength 64
    Assert-ManualShadowBooleanValue -JsonObject $json -Name "Accepted" -Expected $true
    Assert-ManualShadowStringValue -JsonObject $json -Name "ReasonCode" -Expected "langgraph_advisory_accepted" -MaximumLength 64
    Assert-ManualShadowBooleanValue -JsonObject $json -Name "FallbackRequired" -Expected $false
    Assert-ManualShadowBooleanValue -JsonObject $json -Name "NoSqlAuthority" -Expected $true
    Assert-ManualShadowBooleanValue -JsonObject $json -Name "ReadOnly" -Expected $true
    Assert-ManualShadowBooleanValue -JsonObject $json -Name "RequestsCheckpointMutation" -Expected $false
    Assert-ManualShadowBooleanValue -JsonObject $json -Name "RequestsVisibleText" -Expected $false

    foreach ($textName in @("TraceSummary", "ContextContribution")) {
        $text = Get-ManualShadowExactValue -JsonObject $json -Name $textName
        if (($text -is [string]) -eq $false -or [string]::IsNullOrWhiteSpace($text) -or $text.Length -gt 1200) {
            throw "manual_shadow_response_rejected"
        }
    }

    $selectedNodes = @(Get-ManualShadowBoundedArray -JsonObject $json -Name "SelectedNodes" -MinimumCount 1 -MaximumCount 1)
    if (($selectedNodes[0] -is [string]) -eq $false -or $selectedNodes[0] -cne "diagnostics_router") {
        throw "manual_shadow_response_rejected"
    }

    $progress = @(Get-ManualShadowBoundedArray -JsonObject $json -Name "NodeProgress" -MinimumCount 1 -MaximumCount 8)
    Assert-ManualShadowV47Progress -ProgressItems $progress

    $requestedTools = @(Get-ManualShadowBoundedArray -JsonObject $json -Name "RequestedToolNames" -MinimumCount 0 -MaximumCount 0)
    if ($requestedTools.Count -ne 0) {
        throw "manual_shadow_response_rejected"
    }

    return $true
}

function Assert-ManualShadowV47HealthResponse {
    param([object]$Response)

    if ($null -eq $Response) {
        throw "manual_shadow_response_rejected"
    }

    $statusCodeProperties = @($Response.PSObject.Properties | Where-Object { $_.Name -ceq "StatusCode" })
    if ($statusCodeProperties.Count -ne 1 -or ($statusCodeProperties[0].Value -is [int]) -eq $false -or
        [int]$statusCodeProperties[0].Value -ne 200) {
        throw "manual_shadow_response_rejected"
    }

    $json = ConvertTo-ManualShadowStrictJsonObject -Response $Response -MaximumContentChars 4096
    Assert-ManualShadowExactFields -JsonObject $json -ExpectedFields @(
        "ok", "ready", "runtimeMode", "langGraphLoaded", "langGraphVersion",
        "graphCompiled", "contractVersion", "graphVersion", "runtimeInstanceId",
        "configurationFingerprint", "startedAtUnixSeconds"
    )

    Assert-ManualShadowBooleanValue -JsonObject $json -Name "ok" -Expected $true
    Assert-ManualShadowBooleanValue -JsonObject $json -Name "ready" -Expected $true
    Assert-ManualShadowStringValue -JsonObject $json -Name "runtimeMode" -Expected "langgraph" -MaximumLength 32
    Assert-ManualShadowBooleanValue -JsonObject $json -Name "langGraphLoaded" -Expected $true
    Assert-ManualShadowStringValue -JsonObject $json -Name "langGraphVersion" -Expected "0.3.34" -MaximumLength 16
    Assert-ManualShadowBooleanValue -JsonObject $json -Name "graphCompiled" -Expected $true
    Assert-ManualShadowStringValue -JsonObject $json -Name "contractVersion" -Expected "v4.7" -MaximumLength 16
    Assert-ManualShadowStringValue -JsonObject $json -Name "graphVersion" -Expected "dataagent-advisory-v1" -MaximumLength 64

    $runtimeInstanceId = Get-ManualShadowExactValue -JsonObject $json -Name "runtimeInstanceId"
    $parsedRuntimeInstanceId = [guid]::Empty
    if (($runtimeInstanceId -is [string]) -eq $false -or $runtimeInstanceId.Length -ne 36 -or
        [guid]::TryParse($runtimeInstanceId, [ref]$parsedRuntimeInstanceId) -eq $false) {
        throw "manual_shadow_response_rejected"
    }

    $configurationFingerprint = Get-ManualShadowExactValue -JsonObject $json -Name "configurationFingerprint"
    if (($configurationFingerprint -is [string]) -eq $false -or
        $configurationFingerprint -notmatch '^[0-9a-f]{64}$') {
        throw "manual_shadow_response_rejected"
    }

    $startedAtUnixSeconds = Get-ManualShadowExactValue -JsonObject $json -Name "startedAtUnixSeconds"
    if ((($startedAtUnixSeconds -is [int]) -eq $false -and ($startedAtUnixSeconds -is [long]) -eq $false) -or
        [int64]$startedAtUnixSeconds -le 0 -or
        [int64]$startedAtUnixSeconds -gt 4102444800) {
        throw "manual_shadow_response_rejected"
    }

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

function New-V47HandshakeRequest {
    [ordered]@{
        RequestId = "v4-manual-shadow-operator-run"
        SessionId = "v4-manual-shadow"
        TurnId = "manual-shadow-1"
        CallerId = "operator"
        GoalOrQuestion = "Summarize replay evidence for operator review."
        ScenarioContextSummary = "scenario_context=manual_shadow;source_baseline=v3.28"
        RouteScope = "route_present=true;route_allows_query=true"
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

$healthStatusCode = 0
$handshakeStatusCode = 0

try {
    if ($TimeoutMs -le 0) {
        throw "TimeoutMs must be greater than zero."
    }

    $base = Assert-LoopbackBaseUri $BaseUri
    $timeoutSeconds = [Math]::Max(1, [int][Math]::Ceiling($TimeoutMs / 1000.0))
    $request = New-V47HandshakeRequest

    $healthResponse = Invoke-JsonRequest -Method "GET" -Uri (Join-SidecarUri $base "/health") -TimeoutSeconds $timeoutSeconds
    $healthStatusCode = [int]$healthResponse.StatusCode
    Assert-ManualShadowV47HealthResponse $healthResponse | Out-Null
    $handshakeResponse = Invoke-JsonRequest -Method "POST" -Uri (Join-SidecarUri $base "/handshake") -Body $request -TimeoutSeconds $timeoutSeconds
    $handshakeStatusCode = [int]$handshakeResponse.StatusCode
    $handshakeValidated = Assert-ManualShadowV47HandshakeResponse $handshakeResponse

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
