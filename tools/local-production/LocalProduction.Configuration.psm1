function Get-OverallStatus {
    param([hashtable]$Accounts)
    $values = @($Accounts.Values)
    if ($values.Count -eq 0 -or @($values | Where-Object { $_ -eq 'Unavailable' }).Count -eq $values.Count) { return 'unavailable' }
    if (@($values | Where-Object { $_ -ne 'Healthy' }).Count -gt 0) { return 'degraded' }
    return 'healthy'
}

function Test-QZoneLoopbackOperatorUrl {
    param([string]$Value)

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
    return $endpointHost -eq '127.0.0.1' -or $endpointHost -ieq 'localhost' -or $endpointHost -eq '::1'
}

function ConvertTo-QZoneLoopbackOperatorUrl {
    param([string]$Value)

    if (-not (Test-QZoneLoopbackOperatorUrl $Value)) { return $null }

    $uri = [Uri]$Value
    if ($uri.AbsolutePath.EndsWith('/')) { return $uri.AbsoluteUri }

    $builder = [UriBuilder]$uri
    $builder.Path = $uri.AbsolutePath + '/'
    return $builder.Uri.AbsoluteUri
}

function Read-LocalProductionPlan {
    param([string]$Json)
    $plan = $Json | ConvertFrom-Json
    if (@($plan.accounts).Count -ne 2) { throw 'Exactly two loopback accounts are required.' }
    $ids = @($plan.accounts | ForEach-Object id | Sort-Object)
    if (($ids -join ',') -ne 'account-a,account-b') { throw 'Exactly account-a and account-b are required.' }
    $ports = @{}
    $operatorUrls = @{}
    foreach ($slot in $plan.accounts) {
        $uri = [Uri]$slot.oneBotUrl
        if ($uri.Scheme -ne 'ws' -or $uri.Host -notin @('127.0.0.1','localhost','[::1]','::1')) { throw 'OneBot URL must use loopback.' }
        if ($ports.ContainsKey($uri.Port)) { throw 'OneBot ports must be unique.' }; $ports[$uri.Port] = $true
        $expectedOneBotPort = if ($slot.id -eq 'account-a') { 3001 } else { 3002 }
        if ($uri.Port -ne $expectedOneBotPort) { throw 'OneBot ports must match their account roles.' }
        $operatorUrl = ConvertTo-QZoneLoopbackOperatorUrl $slot.qZoneLoopbackOperatorUrl
        if ($null -eq $operatorUrl) { throw 'QZone operator URL must use loopback HTTP.' }
        if ($operatorUrls.ContainsKey($operatorUrl)) { throw 'QZone operator URLs must be unique.' }; $operatorUrls[$operatorUrl] = $true
        $slot.qZoneLoopbackOperatorUrl = $operatorUrl
        foreach ($root in @($slot.runtimeRoot,$slot.storageRoot,$slot.tempRoot)) { if ($root -and -not [IO.Path]::IsPathRooted($root)) { throw 'Roots must be absolute.' } }
    }
    return $plan
}

function Invoke-AccountRecovery {
    param($Slot,[int]$ActiveWorkCount,[DateTimeOffset]$Now)
    if ($ActiveWorkCount -gt 0) {
        $seconds = if ($Slot.drainTimeoutSeconds) { [int]$Slot.drainTimeoutSeconds } else { 90 }
        return [pscustomobject]@{ Action='drain'; Reason='None'; DeadlineUtc=$Now.AddSeconds($seconds) }
    }
    return [pscustomobject]@{ Action='restart-worker'; Reason='RestartRecoveryRequired'; DeadlineUtc=$Now }
}

function Test-OneBotLoopbackTcpReachable {
    param([string]$OneBotUrl)

    $client = $null
    try {
        $uri = [Uri]$OneBotUrl
        if ($uri.Scheme -ne 'ws' -or $uri.Host -notin @('127.0.0.1','localhost','[::1]','::1')) { return $false }

        $client = [Net.Sockets.TcpClient]::new()
        $connectTask = $client.ConnectAsync($uri.Host.Trim('[', ']'), $uri.Port)
        if ($connectTask.Wait(750) -eq $false) { return $false }
        return $client.Connected
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $client) { $client.Dispose() }
    }
}

function Read-AccountRuntimeHealthSnapshot {
    param([string]$StorageRoot,[string]$AccountId)

    if ($AccountId -cnotin @('account-a','account-b')) { return $null }
    $path = Join-Path $StorageRoot 'runtime-health.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }

    try {
        $snapshot = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($snapshot.version -ne 1 -or [string]$snapshot.account -cne $AccountId) { return $null }
        $components = @($snapshot.components)
        if ($components.Count -lt 1 -or $components.Count -gt 4) { return $null }

        $seen = @{}
        $safeComponents = [Collections.Generic.List[object]]::new()
        foreach ($component in $components) {
            $name = [string]$component.component
            $health = [string]$component.health
            $reason = [string]$component.reason
            if ($seen.ContainsKey($name)) { return $null }
            $valid = switch ($name) {
                'onebot' { ($health -eq 'healthy' -and $reason -eq 'OneBotConnected') -or ($health -eq 'unavailable' -and $reason -eq 'OneBotUnavailable') -or ($health -eq 'degraded' -and $reason -eq 'OneBotProbeUnknown') }
                'model' { ($health -eq 'unavailable' -and $reason -eq 'ModelAuthRejected') -or ($health -eq 'degraded' -and $reason -eq 'HealthProbeFailed') }
                'qzone_operator' { ($health -eq 'healthy' -and $reason -eq 'QZoneOperatorReady') -or ($health -eq 'unavailable' -and $reason -eq 'QZoneOperatorUnavailable') }
                'character_activation' { $health -eq 'unavailable' -and $reason -eq 'CharacterActivationFailed' }
                default { $false }
            }
            if (-not $valid) { return $null }
            $seen[$name] = $true
            $safeComponents.Add([pscustomobject]@{ component=$name; health=$health; reason=$reason })
        }

        return [pscustomobject]@{ components=@($safeComponents) }
    }
    catch {
        return $null
    }
}

Export-ModuleMember -Function Get-OverallStatus,Read-LocalProductionPlan,Invoke-AccountRecovery,Test-OneBotLoopbackTcpReachable,Read-AccountRuntimeHealthSnapshot
