param(
    [switch]$Live,
    [switch]$Strict,
    [switch]$Json,
    [string]$VoiceRootPath = 'D:\Alife\Runtime\TTS\voices',
    [string]$ComputerName = '127.0.0.1',
    [int]$XiayuTtsPort = 9880,
    [int]$MixuTtsPort = 9881,
    [string]$AgnesVisionApiKey
)

# Usage modes: default, -Live, -Strict, -Json.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ($Strict -and -not $Live) {
    Write-Output 'Strict mode requires -Live.'
    exit 1
}

function Resolve-AgnesVisionApiKey {
    param(
        [string]$ExplicitValue
    )

    if ([string]::IsNullOrWhiteSpace($ExplicitValue) -eq $false) {
        return $ExplicitValue
    }

    $userValue = [Environment]::GetEnvironmentVariable('ALIFE_AGNES_VISION_API_KEY', 'User')
    if ([string]::IsNullOrWhiteSpace($userValue) -eq $false) {
        return $userValue
    }

    $processValue = [Environment]::GetEnvironmentVariable('ALIFE_AGNES_VISION_API_KEY', 'Process')
    if ([string]::IsNullOrWhiteSpace($processValue) -eq $false) {
        return $processValue
    }

    return $null
}

function Test-TcpPort {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutMilliseconds = 750
    )

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connectTask = $client.ConnectAsync($HostName, $Port)
        $completed = $connectTask.Wait($TimeoutMilliseconds)
        if ($completed -eq $false) {
            return $false
        }

        if ($connectTask.IsFaulted -or $connectTask.IsCanceled) {
            return $false
        }

        return $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Test-ReferenceAudio {
    param(
        [string]$RootPath,
        [string[]]$CandidateRelativePaths
    )

    foreach ($relativePath in $CandidateRelativePaths) {
        $fullPath = Join-Path -Path $RootPath -ChildPath $relativePath
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            return $true
        }
    }

    return $false
}

function Get-StatusText {
    param(
        [bool]$Passed,
        [bool]$Required,
        [bool]$Checked = $true
    )

    if ($Checked -eq $false) {
        return 'SKIPPED'
    }

    if ($Passed) {
        return 'OK'
    }

    if ($Required) {
        return 'MISSING'
    }

    return 'WARN'
}

function New-CheckMetadata {
    param(
        [string]$Field,
        [bool]$Passed,
        [bool]$Checked,
        [bool]$Required,
        [string]$Reason = ''
    )

    [pscustomobject][ordered]@{
        Field = $Field
        Status = Get-StatusText -Passed $Passed -Required $Required -Checked $Checked
        Checked = $Checked
        Required = $Required
        Reason = $Reason
    }
}

$agnesKey = Resolve-AgnesVisionApiKey -ExplicitValue $AgnesVisionApiKey
$agnesVisionKeyConfigured = [string]::IsNullOrWhiteSpace($agnesKey) -eq $false

$xiayuTts9880Reachable = $false
$mixuTts9881Reachable = $false
if ($Live) {
    $xiayuTts9880Reachable = Test-TcpPort -HostName $ComputerName -Port $XiayuTtsPort
    $mixuTts9881Reachable = Test-TcpPort -HostName $ComputerName -Port $MixuTtsPort
}

$xiayuZhRef = Test-ReferenceAudio -RootPath $VoiceRootPath -CandidateRelativePaths @(
    'xiayu\zh\ref.wav',
    'xiayu\zh.wav',
    'xiayu\zh_ref.wav',
    'xiayu\reference_zh.wav',
    'xiayu\zh\reference.wav',
    'XiaYu\zh.wav',
    'XiaYu\zh_ref.wav',
    'XiaYu\reference_zh.wav',
    'XiaYu\zh\reference.wav'
)
$xiayuJaRef = Test-ReferenceAudio -RootPath $VoiceRootPath -CandidateRelativePaths @(
    'xiayu\ja\ref.wav',
    'xiayu\ja.wav',
    'xiayu\ja_ref.wav',
    'xiayu\reference_ja.wav',
    'xiayu\ja\reference.wav',
    'XiaYu\ja.wav',
    'XiaYu\ja_ref.wav',
    'XiaYu\reference_ja.wav',
    'XiaYu\ja\reference.wav'
)
$mixuZhRef = Test-ReferenceAudio -RootPath $VoiceRootPath -CandidateRelativePaths @(
    'mixu\zh\ref.wav',
    'mixu\zh.wav',
    'mixu\zh_ref.wav',
    'mixu\reference_zh.wav',
    'mixu\zh\reference.wav',
    'MiXu\zh.wav',
    'MiXu\zh_ref.wav',
    'MiXu\reference_zh.wav',
    'MiXu\zh\reference.wav'
)
$mixuJaRef = Test-ReferenceAudio -RootPath $VoiceRootPath -CandidateRelativePaths @(
    'mixu\ja\ref.wav',
    'mixu\ja.wav',
    'mixu\ja_ref.wav',
    'mixu\reference_ja.wav',
    'mixu\ja\reference.wav',
    'MiXu\ja.wav',
    'MiXu\ja_ref.wav',
    'MiXu\reference_ja.wav',
    'MiXu\ja\reference.wav'
)

$strictLive = [bool]($Live -and $Strict)
$mode = if ($strictLive) {
    'LiveStrict'
}
elseif ($Live) {
    'Live'
}
else {
    'Default'
}

$ttsSkippedReason = if ($Live) { '' } else { 'Requires -Live.' }
$checks = @(
    New-CheckMetadata -Field 'AgnesVisionKeyConfigured' -Passed $agnesVisionKeyConfigured -Checked $true -Required $strictLive
    New-CheckMetadata -Field 'XiayuTts9880Reachable' -Passed $xiayuTts9880Reachable -Checked ([bool]$Live) -Required $strictLive -Reason $ttsSkippedReason
    New-CheckMetadata -Field 'MixuTts9881Reachable' -Passed $mixuTts9881Reachable -Checked ([bool]$Live) -Required $strictLive -Reason $ttsSkippedReason
    New-CheckMetadata -Field 'XiayuZhRef' -Passed $xiayuZhRef -Checked $true -Required $strictLive
    New-CheckMetadata -Field 'XiayuJaRef' -Passed $xiayuJaRef -Checked $true -Required $strictLive
    New-CheckMetadata -Field 'MixuZhRef' -Passed $mixuZhRef -Checked $true -Required $strictLive
    New-CheckMetadata -Field 'MixuJaRef' -Passed $mixuJaRef -Checked $true -Required $strictLive
)

$result = [ordered]@{
    AgnesVisionKeyConfigured = $agnesVisionKeyConfigured
    XiayuTts9880Reachable = $xiayuTts9880Reachable
    MixuTts9881Reachable = $mixuTts9881Reachable
    XiayuZhRef = $xiayuZhRef
    XiayuJaRef = $xiayuJaRef
    MixuZhRef = $mixuZhRef
    MixuJaRef = $mixuJaRef
}

$requiredFailures = @()
if ($strictLive -and $agnesVisionKeyConfigured -eq $false) { $requiredFailures += 'AgnesVisionKeyConfigured' }
if ($strictLive -and $xiayuTts9880Reachable -eq $false) { $requiredFailures += 'XiayuTts9880Reachable' }
if ($strictLive -and $mixuTts9881Reachable -eq $false) { $requiredFailures += 'MixuTts9881Reachable' }
if ($strictLive -and $xiayuZhRef -eq $false) { $requiredFailures += 'XiayuZhRef' }
if ($strictLive -and $xiayuJaRef -eq $false) { $requiredFailures += 'XiayuJaRef' }
if ($strictLive -and $mixuZhRef -eq $false) { $requiredFailures += 'MixuZhRef' }
if ($strictLive -and $mixuJaRef -eq $false) { $requiredFailures += 'MixuJaRef' }

$result.Mode = $mode
$result.Live = [bool]$Live
$result.Strict = [bool]$Strict
$result.RequiredFailures = @($requiredFailures)
$result.Checks = @($checks)

if ($Json) {
    [pscustomobject]$result | ConvertTo-Json -Depth 3
}
else {
    Write-Output 'QChat Runtime Readiness'
    Write-Output ''
    Write-Output '[Vision]'
    Write-Output ("  {0} Agnes Vision API key configured" -f (Get-StatusText -Passed $agnesVisionKeyConfigured -Required $strictLive))
    Write-Output ''
    Write-Output '[Voice]'
    Write-Output ("  {0} Xiayu TTS port {1}:{2} reachable" -f (Get-StatusText -Passed $xiayuTts9880Reachable -Required $strictLive -Checked ([bool]$Live)), $ComputerName, $XiayuTtsPort)
    Write-Output ("  {0} Mixu TTS port {1}:{2} reachable" -f (Get-StatusText -Passed $mixuTts9881Reachable -Required $strictLive -Checked ([bool]$Live)), $ComputerName, $MixuTtsPort)
    Write-Output ("  {0} Xiayu zh reference audio" -f (Get-StatusText -Passed $xiayuZhRef -Required $strictLive))
    Write-Output ("  {0} Xiayu ja reference audio" -f (Get-StatusText -Passed $xiayuJaRef -Required $strictLive))
    Write-Output ("  {0} Mixu zh reference audio" -f (Get-StatusText -Passed $mixuZhRef -Required $strictLive))
    Write-Output ("  {0} Mixu ja reference audio" -f (Get-StatusText -Passed $mixuJaRef -Required $strictLive))
    Write-Output ''
    Write-Output '[Summary]'

    if ($requiredFailures.Count -gt 0) {
        Write-Output ("Summary: {0} required runtime readiness check(s) missing: {1}" -f $requiredFailures.Count, ($requiredFailures -join ', '))
    }
    elseif ($Live) {
        Write-Output 'Summary: live runtime readiness checks completed.'
    }
    else {
        Write-Output 'Summary: default readiness checks completed; live dependencies were not required.'
    }
}

if ($requiredFailures.Count -gt 0) {
    exit 1
}

exit 0
