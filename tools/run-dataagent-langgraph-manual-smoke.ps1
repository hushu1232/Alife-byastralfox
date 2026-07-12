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
# live_smoke_count=5
# smoke_health_attestation=true
# smoke_valid_advisory=true
# smoke_malformed_json=true
# smoke_oversized_request=true
# smoke_unsupported_content_type=true

function Assert-LoopbackEndpoint {
    param([string]$Value)
    $uri = [System.Uri]::new($Value)
    if ($uri.Scheme -ne "http" -or
        ($uri.Host -ne "127.0.0.1" -and $uri.Host -ne "localhost" -and $uri.Host -ne "::1")) {
        throw "Only loopback hosts are allowed."
    }
    return $uri
}

function Get-EndpointUri {
    param([System.Uri]$BaseUri, [string]$Path)
    $builder = [System.UriBuilder]::new($BaseUri)
    $builder.Path = $Path
    $builder.Query = ""
    return $builder.Uri
}

function Send-Request {
    param(
        [System.Net.Http.HttpClient]$Client,
        [System.Net.Http.HttpMethod]$Method,
        [System.Uri]$Uri,
        [string]$Body = "",
        [string]$ContentType = "application/json"
    )
    $message = [System.Net.Http.HttpRequestMessage]::new($Method, $Uri)
    try {
        if ($Method -ne [System.Net.Http.HttpMethod]::Get) {
            $message.Content = [System.Net.Http.StringContent]::new(
                $Body,
                [System.Text.Encoding]::UTF8,
                $ContentType)
        }
        $response = $Client.SendAsync($message).GetAwaiter().GetResult()
        try {
            return [pscustomobject]@{
                Status = [int]$response.StatusCode
                Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            }
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $message.Dispose()
    }
}

$baseUri = Assert-LoopbackEndpoint $Endpoint
$healthUri = Get-EndpointUri $baseUri "/health"
$handshakeUri = Get-EndpointUri $baseUri "/handshake"
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)

try {
    $healthResult = Send-Request $client ([System.Net.Http.HttpMethod]::Get) $healthUri
    if ($healthResult.Status -ne 200) { throw "Health request did not return 200." }
    $health = $healthResult.Body | ConvertFrom-Json
    if ($health.ready -ne $true -or $health.runtimeMode -ne "langgraph" -or
        $health.langGraphLoaded -ne $true -or $health.langGraphVersion -ne "0.3.34" -or
        $health.graphCompiled -ne $true -or $health.contractVersion -ne "v4.6" -or
        $health.graphVersion -ne "dataagent-advisory-v1") {
        throw "Health attestation is not production-canary ready."
    }
    Write-Output "PASS health attestation"

    $validRequest = @{
        RequestId = "manual-smoke-valid"; SessionId = "manual-session"; TurnId = "turn-1"
        CallerId = "owner"; GoalOrQuestion = "Manual advisory smoke"
        ScenarioContextSummary = "scenario_context=manual_smoke"; RouteScope = "route_present=true"
        QueryConstraints = "status=Active"
        NodeManifests = @(@{
            NodeName = "query_planner"; Purpose = "manual advisory smoke"
            AllowedToolNames = @("dataagent.query_plan.propose")
            DeniedCapabilityMarkers = @("dataagent.query.execute_readonly")
            InputShape = "bounded request"; OutputShape = "advisory response"
            BusinessTerms = @("manual_smoke"); SafetyNotes = "advisory only"
        })
        NoSqlAuthority = $true; ReadOnly = $true; FallbackAvailable = $true
        TraceBudgetChars = 256; ProgressBudget = 2
    }
    $validResult = Send-Request $client ([System.Net.Http.HttpMethod]::Post) $handshakeUri ($validRequest | ConvertTo-Json -Depth 12)
    if ($validResult.Status -ne 200) { throw "Valid advisory did not return 200." }
    $advisory = $validResult.Body | ConvertFrom-Json
    if ($advisory.Accepted -ne $true -or $advisory.FallbackRequired -ne $false -or
        $advisory.NoSqlAuthority -ne $true -or $advisory.ReadOnly -ne $true -or
        $advisory.RequestsCheckpointMutation -ne $false -or
        $advisory.RequestsVisibleText -ne $false -or $advisory.RequestedToolNames.Count -ne 0) {
        throw "Valid advisory returned an unsafe contract."
    }
    Write-Output "PASS valid LangGraph advisory"

    $malformed = Send-Request $client ([System.Net.Http.HttpMethod]::Post) $handshakeUri "{"
    if ($malformed.Status -ne 400) { throw "Malformed JSON did not return 400." }
    Write-Output "PASS malformed JSON returns 400"

    $oversized = Send-Request $client ([System.Net.Http.HttpMethod]::Post) $handshakeUri ("x" * 65537)
    if ($oversized.Status -ne 413) { throw "Oversized request did not return 413." }
    Write-Output "PASS oversized request returns 413"

    $unsupported = Send-Request $client ([System.Net.Http.HttpMethod]::Post) $handshakeUri "{}" "text/plain"
    if ($unsupported.Status -ne 415) { throw "Unsupported content type did not return 415." }
    Write-Output "PASS unsupported content type returns 415"
}
finally {
    $client.Dispose()
}
