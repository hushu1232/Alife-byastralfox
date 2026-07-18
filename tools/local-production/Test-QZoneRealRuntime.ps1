param(
    [ValidateSet('Read','Post','Comment','Like','Image','Delete')]
    [string]$Operation = 'Read',
    [ValidateSet(3001,3002)]
    [int]$Port,
    [switch]$Execute,
    [long]$TargetId,
    [string]$PostId,
    [string]$Content,
    [int]$CommentCount = 20,
    [string]$ImagePath,
    [string]$TopicId,
    [string]$FeedsKey,
    [long]$CreatedAtUnixSeconds,
    [string]$PlanPath
)

$ErrorActionPreference = 'Stop'

function Write-QZoneOperatorResult {
    param(
        [bool]$Succeeded,
        [string]$Message
    )

    [ordered]@{
        operation = $Operation
        port = $Port
        execute = [bool]$Execute
        success = $Succeeded
        message = $Message
    } | ConvertTo-Json -Compress
}

function Test-QZoneLoopbackOperatorEndpoint {
    param(
        [string]$Value,
        [ref]$Endpoint
    )

    $uri = $null
    if ([string]::IsNullOrWhiteSpace($Value) -or
        -not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -ne 'http' -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Query) -or
        -not [string]::IsNullOrEmpty($uri.Fragment)) {
        return $false
    }

    $endpointHost = $uri.Host.Trim('[', ']')
    if ($endpointHost -ne '127.0.0.1' -and -not ($endpointHost -ieq 'localhost') -and $endpointHost -ne '::1') {
        return $false
    }

    if (-not $uri.AbsolutePath.EndsWith('/')) {
        $builder = [UriBuilder]$uri
        $builder.Path = $uri.AbsolutePath + '/'
        $uri = $builder.Uri
    }

    $Endpoint.Value = $uri
    return $true
}

function Get-QZoneOperatorSlot {
    param(
        [string]$Path,
        [int]$SelectedPort
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    try {
        $configurationModulePath = Join-Path $PSScriptRoot 'LocalProduction.Configuration.psm1'
        Import-Module -Name $configurationModulePath -ErrorAction Stop
        $plan = Read-LocalProductionPlan (Get-Content -LiteralPath $Path -Raw)
        $expectedId = if ($SelectedPort -eq 3001) { 'account-a' } else { 'account-b' }
        $slot = @($plan.accounts | Where-Object { $_.id -eq $expectedId }) | Select-Object -First 1
        if ($null -eq $slot) { return $null }

        $oneBotUri = [Uri]$slot.oneBotUrl
        if ($oneBotUri.Scheme -ne 'ws' -or $oneBotUri.Port -ne $SelectedPort) { return $null }

        return $slot
    }
    catch {
        return $null
    }
}

function New-QZoneOperatorRequest {
    switch ($Operation) {
        'Read' {
            if ($TargetId -le 0 -or $CommentCount -lt 0 -or $CommentCount -gt 50) { return $null }
            return [ordered]@{ operation = $Operation; target_id = $TargetId; comment_count = $CommentCount }
        }
        'Post' {
            if ([string]::IsNullOrWhiteSpace($Content)) { return $null }
            return [ordered]@{ operation = $Operation; content = $Content }
        }
        'Comment' {
            if ($TargetId -le 0 -or [string]::IsNullOrWhiteSpace($PostId) -or [string]::IsNullOrWhiteSpace($Content)) { return $null }
            return [ordered]@{ operation = $Operation; target_id = $TargetId; post_id = $PostId; content = $Content }
        }
        'Like' {
            if ($TargetId -le 0 -or [string]::IsNullOrWhiteSpace($PostId)) { return $null }
            return [ordered]@{ operation = $Operation; target_id = $TargetId; post_id = $PostId }
        }
        'Image' {
            if ([string]::IsNullOrWhiteSpace($Content)) { return $null }
            return [ordered]@{ operation = $Operation; content = $Content; source_kind = 'owner_file'; source_value = [IO.Path]::GetFullPath($ImagePath) }
        }
        'Delete' {
            if ($TargetId -le 0 -or
                [string]::IsNullOrWhiteSpace($PostId) -or
                [string]::IsNullOrWhiteSpace($TopicId) -or
                [string]::IsNullOrWhiteSpace($FeedsKey) -or
                $CreatedAtUnixSeconds -le 0) {
                return $null
            }
            return [ordered]@{
                operation = $Operation
                target_id = $TargetId
                post_id = $PostId
                topic_id = $TopicId
                feeds_key = $FeedsKey
                created_at = $CreatedAtUnixSeconds
            }
        }
    }

    return $null
}

if (-not $Execute) {
    [ordered]@{
        operation = $Operation
        port = $Port
        execute = $false
        message = 'Add -Execute to call the selected real QZone operation.'
    } | ConvertTo-Json -Compress
    exit 0
}

if ($Port -notin @(3001, 3002)) {
    Write-QZoneOperatorResult -Succeeded $false -Message 'local_qzone_account_port_invalid'
    exit 1
}

if ($Operation -eq 'Image' -and ([string]::IsNullOrWhiteSpace($ImagePath) -or -not (Test-Path -LiteralPath $ImagePath -PathType Leaf))) {
    Write-QZoneOperatorResult -Succeeded $false -Message 'local_qzone_image_path_unavailable'
    exit 1
}

$request = New-QZoneOperatorRequest
if ($null -eq $request) {
    $reason = if ($Operation -eq 'Delete') { 'local_qzone_delete_metadata_unavailable' } else { 'local_qzone_operator_request_invalid' }
    Write-QZoneOperatorResult -Succeeded $false -Message $reason
    exit 1
}

# The operator script maps the selected OneBot account to a role endpoint. It intentionally keeps
# the established token variable names as names only: the already-running role process owns the value.
$tokenEnvironmentVariableName = if ($Port -eq 3001) {
    'ALIFE_ACCOUNT_A_ONEBOT_TOKEN'
}
else {
    'ALIFE_ACCOUNT_B_ONEBOT_TOKEN'
}
$null = $tokenEnvironmentVariableName

if ([string]::IsNullOrWhiteSpace($PlanPath)) {
    $PlanPath = $env:ALIFE_LOCAL_PRODUCTION_PLAN
}

$slot = Get-QZoneOperatorSlot -Path $PlanPath -SelectedPort $Port
if ($null -eq $slot -or [string]::IsNullOrWhiteSpace($slot.qZoneLoopbackOperatorUrl)) {
    Write-QZoneOperatorResult -Succeeded $false -Message 'local_qzone_runtime_unavailable'
    exit 1
}

$endpoint = $null
if (-not (Test-QZoneLoopbackOperatorEndpoint -Value $slot.qZoneLoopbackOperatorUrl -Endpoint ([ref]$endpoint))) {
    Write-QZoneOperatorResult -Succeeded $false -Message 'local_qzone_operator_endpoint_invalid'
    exit 1
}

Add-Type -AssemblyName System.Net.Http
$MaximumOperatorResponseBytes = 4096
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseProxy = $false
$client = [System.Net.Http.HttpClient]::new($handler, $true)
$response = $null
$httpContent = $null
$requestMessage = $null
$responseStream = $null
$responseBuffer = $null
try {
    $client.Timeout = [TimeSpan]::FromSeconds(10)
    $payload = $request | ConvertTo-Json -Compress
    $httpContent = [System.Net.Http.StringContent]::new($payload, [Text.Encoding]::UTF8, 'application/json')
    $requestMessage = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $endpoint)
    $requestMessage.Content = $httpContent
    $response = $client.SendAsync(
        $requestMessage,
        [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead
    ).GetAwaiter().GetResult()
    if (-not $response.IsSuccessStatusCode -or
        ($null -ne $response.Content.Headers.ContentLength -and $response.Content.Headers.ContentLength -gt $MaximumOperatorResponseBytes)) {
        Write-QZoneOperatorResult -Succeeded $false -Message 'local_qzone_runtime_unavailable'
        exit 1
    }

    $responseStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    $responseBuffer = [IO.MemoryStream]::new()
    $buffer = New-Object byte[] 1024
    while (($read = $responseStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
        if (($responseBuffer.Length + $read) -gt $MaximumOperatorResponseBytes) {
            Write-QZoneOperatorResult -Succeeded $false -Message 'local_qzone_runtime_unavailable'
            exit 1
        }

        $responseBuffer.Write($buffer, 0, $read)
    }

    $operatorResult = ([Text.Encoding]::UTF8.GetString($responseBuffer.ToArray())) | ConvertFrom-Json
    if ($operatorResult.succeeded -eq $true -and $operatorResult.code -eq 'Completed') {
        Write-QZoneOperatorResult -Succeeded $true -Message 'local_qzone_operator_completed'
        exit 0
    }

    Write-QZoneOperatorResult -Succeeded $false -Message 'local_qzone_operator_rejected'
    exit 1
}
catch {
    Write-QZoneOperatorResult -Succeeded $false -Message 'local_qzone_runtime_unavailable'
    exit 1
}
finally {
    if ($null -ne $responseBuffer) { $responseBuffer.Dispose() }
    if ($null -ne $responseStream) { $responseStream.Dispose() }
    if ($null -ne $response) { $response.Dispose() }
    if ($null -ne $requestMessage) { $requestMessage.Dispose() }
    elseif ($null -ne $httpContent) { $httpContent.Dispose() }
    $client.Dispose()
}
