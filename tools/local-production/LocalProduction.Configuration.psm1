function Get-OverallStatus {
    param([hashtable]$Accounts)
    $values = @($Accounts.Values)
    if ($values.Count -eq 0 -or @($values | Where-Object { $_ -eq 'Unavailable' }).Count -eq $values.Count) { return 'unavailable' }
    if (@($values | Where-Object { $_ -ne 'Healthy' }).Count -gt 0) { return 'degraded' }
    return 'healthy'
}

function Read-LocalProductionPlan {
    param([string]$Json)
    $plan = $Json | ConvertFrom-Json
    if (@($plan.accounts).Count -ne 2) { throw 'Exactly two loopback accounts are required.' }
    $ids = @($plan.accounts | ForEach-Object id | Sort-Object)
    if (($ids -join ',') -ne 'account-a,account-b') { throw 'Exactly account-a and account-b are required.' }
    $ports = @{}
    foreach ($slot in $plan.accounts) {
        $uri = [Uri]$slot.oneBotUrl
        if ($uri.Scheme -ne 'ws' -or $uri.Host -notin @('127.0.0.1','localhost','[::1]','::1')) { throw 'OneBot URL must use loopback.' }
        if ($ports.ContainsKey($uri.Port)) { throw 'OneBot ports must be unique.' }; $ports[$uri.Port] = $true
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

Export-ModuleMember -Function Get-OverallStatus,Read-LocalProductionPlan,Invoke-AccountRecovery
