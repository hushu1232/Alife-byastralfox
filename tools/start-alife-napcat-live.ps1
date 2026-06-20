param(
    [string]$NapCatRoot = "D:\NapCat",
    [string]$DotNetPath = "C:\Users\hu shu\.dotnet\dotnet.exe",
    [string]$ClientDll = "D:\Alife\Outputs\Alife.Client\Alife.Client.dll",
    [string]$StoragePath = "D:\Alife\Storage",
    [string]$OneBotUrl = "ws://127.0.0.1:3001",
    [string]$OneBotToken = "",
    [long]$BotId = 3340947887,
    [long]$OwnerId = 3045846738,
    [long]$MotherId = 3658431719,
    [long]$GroupId = 867165927,
    [long]$PrivateTestUserId = 3425085583,
    [int]$OneBotWaitSeconds = 90,
    [switch]$Build,
    [switch]$SkipNapCat,
    [switch]$SkipAlife,
    [switch]$RunLiveSmoke,
    [switch]$RunModelLoop,
    [switch]$RestartNapCat,
    [switch]$ContinueOnLiveTestFailure,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[AstralFox] $Message"
}

function Get-OneBotEndpoint {
    param([string]$Url)

    $uri = [Uri]$Url
    $hostName = if ([string]::IsNullOrWhiteSpace($uri.Host)) { "127.0.0.1" } else { $uri.Host }
    $port = if ($uri.Port -gt 0) { $uri.Port } else { 3001 }
    [pscustomobject]@{ Host = $hostName; Port = $port }
}

function Wait-TcpPort {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::Now.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::Now -lt $deadline) {
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $task = $client.ConnectAsync($HostName, $Port)
            if ($task.Wait(1000) -and $client.Connected) {
                return $true
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
        finally {
            $client.Dispose()
        }

        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Show-QChatLiveRegressionChecklist {
    Write-Step "QQ live regression checklist"
    Write-Step "Bot=$BotId Owner=$OwnerId Mother=$MotherId Group=$GroupId PrivateTestUser=$PrivateTestUserId OneBot=$OneBotUrl"
    Write-Step "QQ-L1 Owner private text: expect one complete model reply."
    Write-Step "QQ-L2 Group @ bot: expect one complete model reply."
    Write-Step "QQ-L3 Group unmentioned chatter: expect balanced-mode suppression most of the time."
    Write-Step "QQ-L4 Group image/sticker without @: expect only occasional passive reply, never every image."
    Write-Step "QQ-L5 Owner sleep command: expect one persona acknowledgement, then quiet mode."
    Write-Step "QQ-L6 Mother wake command: expect quiet-mode wake only, no owner permissions."
    Write-Step "QQ-L7 Audit outgoing QQ text: no internal status, policy label, or diagnostic reason."
    Write-Step "Record results without OneBot tokens, API keys, cookies, or private message content."
}

function Resolve-NapCatShell {
    param([string]$Root)

    if (-not (Test-Path -LiteralPath $Root)) {
        throw "NapCat root was not found: $Root"
    }

    $oneKey = Get-ChildItem -LiteralPath $Root -Directory -Filter "NapCat.Shell.Windows.OneKey*" |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if ($null -eq $oneKey) {
        throw "No NapCat.Shell.Windows.OneKey directory was found under $Root"
    }

    $shell = Get-ChildItem -LiteralPath $oneKey.FullName -Directory -Filter "NapCat.*.Shell" |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if ($null -eq $shell) {
        $bootmain = Join-Path $oneKey.FullName "bootmain"
        if (Test-Path -LiteralPath $bootmain) {
            $shell = Get-Item -LiteralPath $bootmain
        }
    }
    if ($null -eq $shell) {
        throw "No NapCat shell or bootmain directory was found under $($oneKey.FullName)"
    }

    $quick = Join-Path $shell.FullName "napcat.quick.bat"
    $normal = Join-Path $shell.FullName "napcat.bat"
    $entry = if (Test-Path -LiteralPath $quick) { $quick } else { $normal }
    if (-not (Test-Path -LiteralPath $entry)) {
        throw "No napcat.quick.bat or napcat.bat was found under $($shell.FullName)"
    }

    [pscustomobject]@{
        OneKeyDirectory = $oneKey.FullName
        ShellDirectory = $shell.FullName
        EntryScript = $entry
    }
}

function Start-NapCat {
    param([object]$NapCat)

    if ($RestartNapCat) {
        Write-Step "Stopping existing NapCat shell processes before restart"
        if (-not $DryRun) {
            Get-Process NapCatWinBootMain -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue
            Get-Process QQ -ErrorAction SilentlyContinue |
                Where-Object { $_.Path -like "$($NapCat.ShellDirectory)*" } |
                Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
        }
    }

    Write-Step "Starting NapCat: $($NapCat.EntryScript)"
    if ($DryRun) {
        return
    }

    Start-Process -FilePath $NapCat.EntryScript -WorkingDirectory $NapCat.ShellDirectory
}

function Start-AlifeClient {
    if (-not (Test-Path -LiteralPath $DotNetPath)) {
        throw "Required .NET runtime was not found: $DotNetPath"
    }
    if (-not (Test-Path -LiteralPath $ClientDll)) {
        throw "Alife client build output was not found: $ClientDll"
    }

    $workingDirectory = [System.IO.Path]::GetDirectoryName($ClientDll)
    Set-AlifeStoragePath
    Write-Step "Starting Alife Client: $ClientDll"
    if ($DryRun) {
        return
    }

    Start-Process -FilePath $DotNetPath -ArgumentList @($ClientDll) -WorkingDirectory $workingDirectory
}

function Set-AlifeStoragePath {
    if ([string]::IsNullOrWhiteSpace($StoragePath)) {
        return
    }

    $clientDirectory = Split-Path -Parent $ClientDll
    $outputsDirectory = Split-Path -Parent $clientDirectory
    $rootDirectory = Split-Path -Parent $outputsDirectory
    $runtimeDirectory = Join-Path $rootDirectory "Runtime"
    $storageFile = Join-Path $runtimeDirectory "storage_path.txt"
    if (-not (Test-Path -LiteralPath $StoragePath)) {
        New-Item -ItemType Directory -Path $StoragePath -Force | Out-Null
    }
    if (-not (Test-Path -LiteralPath $runtimeDirectory)) {
        New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
    }

    Set-Content -LiteralPath $storageFile -Value $StoragePath -Encoding UTF8
    Write-Step "Alife storage path: $StoragePath"
}

function Invoke-QChatLiveTest {
    param([string]$Filter)

    $env:ALIFE_QCHAT_LIVE_OWNER_NOTIFICATION = "1"
    $env:ALIFE_QCHAT_LIVE_URL = $OneBotUrl
    $env:ALIFE_QCHAT_LIVE_TOKEN = $OneBotToken
    $env:ALIFE_QCHAT_LIVE_BOT_ID = [string]$BotId
    $env:ALIFE_QCHAT_LIVE_OWNER_ID = [string]$OwnerId
    $env:ALIFE_QCHAT_LIVE_GROUP_ID = [string]$GroupId
    $env:ALIFE_QCHAT_LIVE_PRIVATE_TEST_USER_ID = [string]$PrivateTestUserId

    Write-Step "Running QChat live test filter: $Filter"
    if ($DryRun) {
        return
    }

    & $DotNetPath test "D:\Alife\Tests\Alife.Test.QChat\Alife.Test.QChat.csproj" --no-build --no-restore -m:1 --filter $Filter
    if ($LASTEXITCODE -ne 0) {
        if ($ContinueOnLiveTestFailure) {
            Write-Warning "QChat live test failed with exit code $LASTEXITCODE"
            return
        }

        throw "QChat live test failed with exit code $LASTEXITCODE"
    }
}

Write-Step "NapCat root: $NapCatRoot"
$napcat = Resolve-NapCatShell -Root $NapCatRoot
Write-Step "NapCat shell: $($napcat.ShellDirectory)"
Write-Step "NapCat entry: $($napcat.EntryScript)"

if ($Build) {
    Write-Step "Building Alife solution"
    if (-not $DryRun) {
        & $DotNetPath build "D:\Alife\Alife.slnx" --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "Alife build failed with exit code $LASTEXITCODE"
        }
    }
}

if (-not $SkipNapCat) {
    $endpoint = Get-OneBotEndpoint -Url $OneBotUrl
    $alreadyReachable = $false
    if (-not $DryRun -and -not $RestartNapCat) {
        $alreadyReachable = Wait-TcpPort -HostName $endpoint.Host -Port $endpoint.Port -TimeoutSeconds 2
    }

    if ($alreadyReachable) {
        Write-Step "OneBot endpoint is already reachable; NapCat start skipped. Use -RestartNapCat to restart it."
    }
    else {
        Start-NapCat -NapCat $napcat
    }

    Write-Step "Waiting for OneBot endpoint $($endpoint.Host):$($endpoint.Port)"
    if (-not $DryRun) {
        $ready = Wait-TcpPort -HostName $endpoint.Host -Port $endpoint.Port -TimeoutSeconds $OneBotWaitSeconds
        if (-not $ready) {
            throw "OneBot endpoint did not become reachable within $OneBotWaitSeconds seconds: $OneBotUrl"
        }
        Write-Step "OneBot endpoint is reachable"
    }
}

if (-not $SkipAlife) {
    Start-AlifeClient
}

if ($RunLiveSmoke -or $RunModelLoop) {
    Show-QChatLiveRegressionChecklist
}

if ($RunLiveSmoke) {
    Invoke-QChatLiveTest -Filter "LiveDirectOneBotSendDiagnostics|LiveSentenceStreamingDoesNotHardCutUnfinishedQqText|LiveGroupMembersAndGroupFileUpload"
}

if ($RunModelLoop) {
    $env:ALIFE_QCHAT_LIVE_REAL_MODEL_LOOP = "1"
    Invoke-QChatLiveTest -Filter "LiveRealOneBotIncomingMessagesTriggerModelReplies"
}

Write-Step "Done."
