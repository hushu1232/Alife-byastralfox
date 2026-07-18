$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'Test-QZoneRealRuntime.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("alife-qzone-operator-" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$fakeOperatorPath = Join-Path $testRoot 'Fake-QZoneLoopbackOperator.ps1'

@'
param(
    [string]$Prefix,
    [string]$RequestPath,
    [string]$RequestTargetPath,
    [string]$ReadyPath,
    [string]$CompletedPath,
    [switch]$ChunkedOversizedResponse
)

$ErrorActionPreference = 'Stop'
$uri = [Uri]$Prefix
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $uri.Port)
$listener.Start()
[IO.File]::WriteAllText($ReadyPath, 'ready')
try {
    $client = $listener.AcceptTcpClient()
    try {
        $stream = $client.GetStream()
        try {
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
            try {
                $contentLength = 0
                $requestLine = $reader.ReadLine()
                while ($true) {
                    $header = $reader.ReadLine()
                    if ([string]::IsNullOrEmpty($header)) { break }
                    if ($header -match '(?i)^Content-Length:\s*(\d+)\s*$') { $contentLength = [int]$matches[1] }
                }

                $characters = New-Object char[] $contentLength
                $totalRead = 0
                while ($totalRead -lt $contentLength) {
                    $read = $reader.Read($characters, $totalRead, $contentLength - $totalRead)
                    if ($read -le 0) { break }
                    $totalRead += $read
                }
                $body = [string]::new($characters, 0, $totalRead)
            }
            finally { $reader.Dispose() }

            [IO.File]::WriteAllText($RequestPath, $body, [Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllText($RequestTargetPath, ($requestLine -split ' ')[1])
            if ($ChunkedOversizedResponse) {
                $responseHeader = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nTransfer-Encoding: chunked`r`nConnection: close`r`n`r`n")
                $chunk = [Text.Encoding]::UTF8.GetBytes([string]::new([char]'x', 256))
                $stream.Write($responseHeader, 0, $responseHeader.Length)
                for ($index = 0; $index -lt 256; $index++) {
                    $chunkHeader = [Text.Encoding]::ASCII.GetBytes(("{0:X}`r`n" -f $chunk.Length))
                    $stream.Write($chunkHeader, 0, $chunkHeader.Length)
                    $stream.Write($chunk, 0, $chunk.Length)
                    $stream.Write([byte[]](13, 10), 0, 2)
                    $stream.Flush()
                    Start-Sleep -Milliseconds 5
                }
                $terminator = [Text.Encoding]::ASCII.GetBytes("0`r`n`r`n")
                $stream.Write($terminator, 0, $terminator.Length)
                $stream.Flush()
                [IO.File]::WriteAllText($CompletedPath, 'completed')
            }
            else {
                $response = [Text.Encoding]::UTF8.GetBytes('{"succeeded":true,"code":"Completed"}')
                $responseHeader = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($response.Length)`r`nConnection: close`r`n`r`n")
                $stream.Write($responseHeader, 0, $responseHeader.Length)
                $stream.Write($response, 0, $response.Length)
                $stream.Flush()
            }
        }
        finally { $stream.Dispose() }
    }
    finally { $client.Dispose() }
}
finally {
    $listener.Stop()
}
'@ | Set-Content -LiteralPath $fakeOperatorPath -Encoding UTF8

function Assert-Equal($Actual, $Expected) {
    if ($Actual -ne $Expected) { throw "Expected '$Expected', got '$Actual'." }
}

function Assert-True($Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}

function Invoke-Operator([string[]]$Arguments) {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath @Arguments 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = @($output) }
}

function New-AvailableLoopbackPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Start-FakeOperator {
    param(
        [string]$Name,
        [switch]$ChunkedOversizedResponse
    )

    $port = New-AvailableLoopbackPort
    $prefix = "http://127.0.0.1:$port/qzone/"
    $capturePath = Join-Path $testRoot "$Name-request.json"
    $requestTargetPath = Join-Path $testRoot "$Name-request-target.txt"
    $readyPath = Join-Path $testRoot "$Name-ready.txt"
    $completedPath = Join-Path $testRoot "$Name-completed.txt"
    $command = "& '{0}' -Prefix '{1}' -RequestPath '{2}' -ReadyPath '{3}'" -f @(
        ($fakeOperatorPath -replace "'", "''")
        ($prefix -replace "'", "''")
        ($capturePath -replace "'", "''")
        ($readyPath -replace "'", "''")
    )
    $command += " -RequestTargetPath '{0}'" -f ($requestTargetPath -replace "'", "''")
    $command += " -CompletedPath '{0}'" -f ($completedPath -replace "'", "''")
    if ($ChunkedOversizedResponse) { $command += ' -ChunkedOversizedResponse' }
    $argumentList = @(
        '-ExecutionPolicy',
        'Bypass',
        '-Command',
        ('"{0}"' -f $command)
    ) -join ' '
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'powershell.exe'
    $startInfo.Arguments = $argumentList
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($startInfo)

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $readyPath)) {
        if ($process.HasExited) {
            $errorText = $process.StandardError.ReadToEnd()
            throw "Fake loopback operator exited before becoming ready: $errorText"
        }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Fake loopback operator did not become ready.' }
        Start-Sleep -Milliseconds 25
    }

    return [pscustomobject]@{ Prefix = $prefix; CapturePath = $capturePath; RequestTargetPath = $requestTargetPath; CompletedPath = $completedPath; Process = $process }
}

function Stop-FakeOperator($Server) {
    try {
        if (-not $Server.Process.WaitForExit(6000)) {
            Stop-Process -Id $Server.Process.Id -Force
            $Server.Process.WaitForExit()
        }
    }
    finally {
        $Server.Process.Dispose()
    }
}

function Write-OperatorPlan([string]$AccountAOperatorUrl, [string]$AccountBOperatorUrl, [string]$Name) {
    $planPath = Join-Path $testRoot "$Name-plan.json"
    [ordered]@{
        accounts = @(
            [ordered]@{
                id = 'account-a'
                oneBotUrl = 'ws://127.0.0.1:3001'
                qZoneLoopbackOperatorUrl = $AccountAOperatorUrl
            },
            [ordered]@{
                id = 'account-b'
                oneBotUrl = 'ws://127.0.0.1:3002'
                qZoneLoopbackOperatorUrl = $AccountBOperatorUrl
            }
        )
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $planPath -Encoding UTF8
    return $planPath
}

try {
    $default = Invoke-Operator @('-Operation', 'Read', '-Port', '3001')
    Assert-Equal $default.ExitCode 0
    $defaultResult = ($default.Output -join [Environment]::NewLine) | ConvertFrom-Json
    Assert-Equal $defaultResult.operation 'Read'
    Assert-Equal $defaultResult.port 3001
    Assert-Equal $defaultResult.execute $false
    Assert-True ($defaultResult.message -match 'Add -Execute') 'Default preview must explain that execution was not requested.'

    $deletePreview = Invoke-Operator @('-Operation', 'Delete', '-Port', '3001')
    Assert-Equal $deletePreview.ExitCode 0
    $deletePreviewResult = ($deletePreview.Output -join [Environment]::NewLine) | ConvertFrom-Json
    Assert-Equal $deletePreviewResult.operation 'Delete'
    Assert-Equal $deletePreviewResult.port 3001
    Assert-Equal $deletePreviewResult.execute $false
    Assert-True ($deletePreviewResult.message -match 'Add -Execute') 'Delete preview must remain inert until execution is explicitly requested.'

    $missingImage = Invoke-Operator @('-Operation', 'Image', '-Port', '3001', '-Execute', '-ImagePath', 'C:\not-a-real-qzone-image.png')
    Assert-Equal $missingImage.ExitCode 1
    $missingImageResult = ($missingImage.Output -join [Environment]::NewLine) | ConvertFrom-Json
    Assert-Equal $missingImageResult.message 'local_qzone_image_path_unavailable'
    Assert-True (($missingImage.Output -join [Environment]::NewLine) -notmatch 'not-a-real-qzone-image') 'Image-path failure must not echo the caller path.'

    $source = Get-Content -LiteralPath $scriptPath -Raw
    Assert-True ($source -match 'ALIFE_ACCOUNT_A_ONEBOT_TOKEN') 'Account A token variable name is required.'
    Assert-True ($source -match 'ALIFE_ACCOUNT_B_ONEBOT_TOKEN') 'Account B token variable name is required.'
    Assert-True ($source -match 'HttpClientHandler') 'Forwarding must use a local HTTP client with redirect handling under script control.'
    Assert-True ($source -match 'AllowAutoRedirect\s*=\s*\$false') 'Forwarding must not follow a loopback endpoint redirect to another host.'
    Assert-True ($source -match 'UseProxy\s*=\s*\$false') 'Forwarding must not route a local operator request through a configured proxy.'
    Assert-True ($source -match 'MaximumOperatorResponseBytes') 'Forwarding must bound an unknown-length local operator response.'
    Assert-True ($source -match 'ReadAsStreamAsync') 'Forwarding must enforce the response bound while streaming an unknown-length body.'
    Assert-True ($source -match 'ResponseHeadersRead') 'Forwarding must receive only headers before enforcing the streamed response bound.'
    Assert-True ($source -match 'Read-LocalProductionPlan') 'Forwarding must validate the complete two-role production plan before selecting an endpoint.'
    Assert-True ($source -notmatch '(?i)Invoke-WebRequest|Invoke-RestMethod|ClientWebSocket|Start-Process|GetEnvironmentVariable\([^)]*ONEBOT_TOKEN|\$env:ALIFE_ACCOUNT_[AB]_ONEBOT_TOKEN') 'Operator script must not start a runtime, open OneBot/QZone directly, or read a token value.'

    $accountA = Start-FakeOperator 'account-a'
    $accountB = Start-FakeOperator 'account-b'
    try {
        $planPath = Write-OperatorPlan $accountA.Prefix $accountB.Prefix 'forwarding'
        [Environment]::SetEnvironmentVariable('ALIFE_ACCOUNT_A_ONEBOT_TOKEN', 'token-must-never-appear', 'Process')

        $forwardedA = Invoke-Operator @('-Operation', 'Read', '-Port', '3001', '-Execute', '-TargetId', '10001', '-PlanPath', $planPath)
        $forwardedB = Invoke-Operator @('-Operation', 'Read', '-Port', '3002', '-Execute', '-TargetId', '20002', '-PlanPath', $planPath)
        Assert-Equal $forwardedA.ExitCode 0
        Assert-Equal $forwardedB.ExitCode 0
    }
    finally {
        Stop-FakeOperator $accountA
        Stop-FakeOperator $accountB
    }

    $requestA = (Get-Content -LiteralPath $accountA.CapturePath -Raw) | ConvertFrom-Json
    $requestB = (Get-Content -LiteralPath $accountB.CapturePath -Raw) | ConvertFrom-Json
    Assert-Equal $requestA.operation 'Read'
    Assert-Equal $requestA.target_id 10001
    Assert-Equal $requestB.operation 'Read'
    Assert-Equal $requestB.target_id 20002
    Assert-Equal ((@($requestA.PSObject.Properties.Name) -join ',')) 'operation,target_id,comment_count'
    Assert-True ((($forwardedA.Output + $forwardedB.Output) -join [Environment]::NewLine) -notmatch 'token-must-never-appear') 'Operator output must not reveal a token environment value.'
    Assert-True (((Get-Content -LiteralPath $accountA.CapturePath -Raw) -notmatch 'token-must-never-appear')) 'Operator request must not contain a token environment value.'

    $imagePath = Join-Path $testRoot 'operator-image.png'
    [IO.File]::WriteAllBytes($imagePath, [byte[]](1, 2, 3))
    $imageServer = Start-FakeOperator 'image'
    try {
        $imagePlanPath = Write-OperatorPlan $imageServer.Prefix 'http://127.0.0.1:59999/qzone/' 'image'
        $imageForward = Invoke-Operator @('-Operation', 'Image', '-Port', '3001', '-Execute', '-Content', 'local image', '-ImagePath', $imagePath, '-PlanPath', $imagePlanPath)
        Assert-Equal $imageForward.ExitCode 0
    }
    finally {
        Stop-FakeOperator $imageServer
    }

    $imageRequest = (Get-Content -LiteralPath $imageServer.CapturePath -Raw) | ConvertFrom-Json
    Assert-Equal ((@($imageRequest.PSObject.Properties.Name) -join ',')) 'operation,content,source_kind,source_value'
    Assert-Equal $imageRequest.operation 'Image'
    Assert-Equal $imageRequest.content 'local image'
    Assert-Equal $imageRequest.source_kind 'owner_file'
    Assert-Equal $imageRequest.source_value ([IO.Path]::GetFullPath($imagePath))

    $incompletePlanServer = Start-FakeOperator 'incomplete-plan'
    try {
        $incompletePlanPath = Join-Path $testRoot 'incomplete-plan.json'
        [ordered]@{
            accounts = @(
                [ordered]@{
                    id = 'account-a'
                    oneBotUrl = 'ws://127.0.0.1:3001'
                    qZoneLoopbackOperatorUrl = $incompletePlanServer.Prefix
                }
            )
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $incompletePlanPath -Encoding UTF8

        $incompletePlanResult = Invoke-Operator @('-Operation', 'Read', '-Port', '3001', '-Execute', '-TargetId', '10001', '-PlanPath', $incompletePlanPath)
        Assert-Equal $incompletePlanResult.ExitCode 1
        Assert-Equal ((($incompletePlanResult.Output -join [Environment]::NewLine) | ConvertFrom-Json).message) 'local_qzone_runtime_unavailable'
    }
    finally {
        Stop-FakeOperator $incompletePlanServer
    }
    Assert-True (-not (Test-Path -LiteralPath $incompletePlanServer.CapturePath)) 'An incomplete role plan must not receive an operator request.'

    $slashlessServer = Start-FakeOperator 'slashless'
    try {
        $slashlessPlanPath = Write-OperatorPlan $slashlessServer.Prefix.TrimEnd('/') 'http://127.0.0.1:59999/qzone/' 'slashless'
        $slashlessResult = Invoke-Operator @('-Operation', 'Read', '-Port', '3001', '-Execute', '-TargetId', '10001', '-PlanPath', $slashlessPlanPath)
        Assert-Equal $slashlessResult.ExitCode 0
    }
    finally {
        Stop-FakeOperator $slashlessServer
    }
    Assert-Equal (Get-Content -LiteralPath $slashlessServer.RequestTargetPath -Raw) '/qzone/'

    $chunkedServer = Start-FakeOperator 'chunked' -ChunkedOversizedResponse
    try {
        $chunkedPlanPath = Write-OperatorPlan $chunkedServer.Prefix 'http://127.0.0.1:59999/qzone/' 'chunked'
        $chunkedResult = Invoke-Operator @('-Operation', 'Read', '-Port', '3001', '-Execute', '-TargetId', '10001', '-PlanPath', $chunkedPlanPath)
        Assert-Equal $chunkedResult.ExitCode 1
        $chunkedResultJson = ($chunkedResult.Output -join [Environment]::NewLine) | ConvertFrom-Json
        Assert-Equal $chunkedResultJson.message 'local_qzone_runtime_unavailable'
        Assert-True (($chunkedResult.Output -join [Environment]::NewLine) -notmatch 'xxxxxxxx') 'A rejected chunked response must not be printed.'
    }
    finally {
        Stop-FakeOperator $chunkedServer
    }

    $deleteServer = Start-FakeOperator 'delete'
    try {
        $deletePlanPath = Write-OperatorPlan $deleteServer.Prefix 'http://127.0.0.1:59999/qzone/' 'delete'
        $partialDelete = Invoke-Operator @('-Operation', 'Delete', '-Port', '3001', '-Execute', '-TargetId', '10001', '-PostId', 'post-1', '-TopicId', 'topic-1', '-FeedsKey', 'feed-1', '-PlanPath', $deletePlanPath)
        Assert-Equal $partialDelete.ExitCode 1
        $partialDeleteResult = ($partialDelete.Output -join [Environment]::NewLine) | ConvertFrom-Json
        Assert-Equal $partialDeleteResult.message 'local_qzone_delete_metadata_unavailable'

        $fullDelete = Invoke-Operator @('-Operation', 'Delete', '-Port', '3001', '-Execute', '-TargetId', '10001', '-PostId', 'post-1', '-TopicId', 'topic-1', '-FeedsKey', 'feed-1', '-CreatedAtUnixSeconds', '42', '-PlanPath', $deletePlanPath)
        Assert-Equal $fullDelete.ExitCode 0
    }
    finally {
        Stop-FakeOperator $deleteServer
    }

    $deleteRequest = (Get-Content -LiteralPath $deleteServer.CapturePath -Raw) | ConvertFrom-Json
    Assert-Equal ((@($deleteRequest.PSObject.Properties.Name) -join ',')) 'operation,target_id,post_id,topic_id,feeds_key,created_at'
    Assert-Equal $deleteRequest.created_at 42

    $missingOperatorPlanPath = Write-OperatorPlan '' 'http://127.0.0.1:59999/qzone/' 'missing-operator'
    $unavailable = Invoke-Operator @('-Operation', 'Read', '-Port', '3001', '-Execute', '-TargetId', '10001', '-PlanPath', $missingOperatorPlanPath)
    Assert-Equal $unavailable.ExitCode 1
    $unavailableResult = ($unavailable.Output -join [Environment]::NewLine) | ConvertFrom-Json
    Assert-Equal $unavailableResult.message 'local_qzone_runtime_unavailable'

    $externalOperatorPlanPath = Write-OperatorPlan 'http://example.invalid:5101/qzone/' 'http://127.0.0.1:59999/qzone/' 'external-operator'
    $external = Invoke-Operator @('-Operation', 'Read', '-Port', '3001', '-Execute', '-TargetId', '10001', '-PlanPath', $externalOperatorPlanPath)
    Assert-Equal $external.ExitCode 1
    $externalResult = ($external.Output -join [Environment]::NewLine) | ConvertFrom-Json
    Assert-Equal $externalResult.message 'local_qzone_runtime_unavailable'
    Assert-True (($external.Output -join [Environment]::NewLine) -notmatch 'example.invalid') 'Rejected endpoint output must not disclose the configured host.'

}
finally {
    [Environment]::SetEnvironmentVariable('ALIFE_ACCOUNT_A_ONEBOT_TOKEN', $null, 'Process')
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
