param(
  [string]$NapCatRoot='D:\NapCat',
  [ValidateSet(0,3001,3002)][int]$AccountPort=0,
  [ValidateSet('Quick','Qr')][string]$LoginMode='Quick',
  [switch]$Start,
  [switch]$Interactive,
  [switch]$RestartLaunchers,
  [int]$ProbeTimeoutSeconds=5
)
$ErrorActionPreference='Stop'

function Test-PathWithin {
  param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$Root)

  $fullPath=[IO.Path]::GetFullPath($Path)
  $fullRoot=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
  return $fullPath.StartsWith($fullRoot,[StringComparison]::OrdinalIgnoreCase)
}

function Get-OneBotEndpointForPort {
  param([Parameter(Mandatory)][string]$RoleRoot,[Parameter(Mandatory)][int]$Port)

  $matches=@()
  foreach($file in @(Get-ChildItem -LiteralPath $RoleRoot -Recurse -File -Filter 'onebot11_*.json')){
    try {$config=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json -ErrorAction Stop}catch{throw 'A role OneBot configuration is invalid.'}
    foreach($server in @($config.network.websocketServers)){
      if($null-eq$server){continue}
      if($server.PSObject.Properties.Match('enable').Count-gt0-and-not[bool]$server.enable){continue}
      try {$serverPort=[int]$server.port}catch{continue}
      if($serverPort-ne$Port){continue}
      $hostName=[string]$server.host
      if($hostName-notin @('127.0.0.1','localhost','::1')){throw 'OneBot host must use loopback.'}
      $matches+=[pscustomobject]@{Host=$hostName;Port=$serverPort}
    }
  }
  if($matches.Count-ne1){throw 'Each role must have exactly one enabled OneBot endpoint for its manifest port.'}
  return $matches[0]
}

function Get-NapCatDualAccountPlan {
  param([Parameter(Mandatory)][string]$NapCatRoot)

  if(-not(Test-Path -LiteralPath $NapCatRoot -PathType Container)){throw 'NapCat root was not found.'}
  $manifests=@(Get-ChildItem -LiteralPath $NapCatRoot -Recurse -File -Filter 'alife-napcat-role-host.json'|Sort-Object FullName)
  if($manifests.Count-ne2){throw 'Exactly two role manifests are required.'}

  $slots=@()
  foreach($file in $manifests){
    try {$manifest=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json -ErrorAction Stop}catch{throw 'A role manifest is invalid.'}
    $roleRoot=[string]$manifest.RoleRoot
    $launchPath=[string]$manifest.LaunchPath
    try {$port=[int]$manifest.OneBotPort}catch{throw 'A role manifest port is invalid.'}
    if([string]::IsNullOrWhiteSpace($roleRoot)-or[string]::IsNullOrWhiteSpace($launchPath)){throw 'A role manifest is incomplete.'}
    if(-not(Test-PathWithin -Path $roleRoot -Root $NapCatRoot)){throw 'Role root must stay under the NapCat root.'}
    if(-not(Test-PathWithin -Path $launchPath -Root $roleRoot)){throw 'Role launch path must stay under its role root.'}
    if(-not(Test-Path -LiteralPath $launchPath -PathType Leaf)){throw 'Role launch path was not found.'}
    if($port-notin @(3001,3002)){throw 'Role port must be 3001 or 3002.'}
    $quickEntry=Join-Path $roleRoot 'napcat.quick.bat'
    if(-not(Test-Path -LiteralPath $quickEntry -PathType Leaf)){throw 'Role quick launcher was not found.'}
    $qrEntries=@(Get-ChildItem -LiteralPath $roleRoot -Recurse -File -Filter 'launcher-win10-user.bat'|Sort-Object FullName)
    if($qrEntries.Count-ne1){throw 'Each role must have exactly one QR launcher.'}
    $endpoint=Get-OneBotEndpointForPort -RoleRoot $roleRoot -Port $port
    $tokenEnvironmentName=if($port-eq3001){'ALIFE_ACCOUNT_A_ONEBOT_TOKEN'}else{'ALIFE_ACCOUNT_B_ONEBOT_TOKEN'}
    $slots+=[pscustomobject]@{
      RoleRoot=[IO.Path]::GetFullPath($roleRoot)
      LaunchPath=[IO.Path]::GetFullPath($launchPath)
      QuickEntry=[IO.Path]::GetFullPath($quickEntry)
      QrEntry=$qrEntries[0].FullName
      Host=$endpoint.Host
      Port=$endpoint.Port
      TokenEnvironmentName=$tokenEnvironmentName
    }
  }
  if(@($slots.RoleRoot|Sort-Object -Unique).Count-ne2){throw 'Exactly two unique role roots are required.'}
  if(@($slots.LaunchPath|Sort-Object -Unique).Count-ne2){throw 'Exactly two unique role launch paths are required.'}
  if(@($slots.Port|Sort-Object -Unique).Count-ne2){throw 'Exactly two unique role ports are required.'}
  return @($slots|Sort-Object Port)
}

function Test-NapCatHost {
  param([Parameter(Mandatory)][object]$Slot)

  try{
    return $null-ne(Get-CimInstance Win32_Process -Filter "Name='NapCatWinBootMain.exe'"|
      Where-Object{$_.ExecutablePath-eq$Slot.LaunchPath}|
      Select-Object -First 1)
  }catch{return $false}
}

function Test-OneBotPort {
  param([Parameter(Mandatory)][string]$HostName,[Parameter(Mandatory)][int]$Port,[Parameter(Mandatory)][int]$TimeoutSeconds)

  $client=[Net.Sockets.TcpClient]::new()
  try{$task=$client.ConnectAsync($HostName,$Port);return $task.Wait($TimeoutSeconds*1000)-and$client.Connected}
  catch{return $false}
  finally{$client.Dispose()}
}

function ConvertTo-OneBotStatus {
  param([string]$Payload)

  try{$online=($Payload|ConvertFrom-Json -ErrorAction Stop).data.online}catch{return 'unknown'}
  if($online-is[bool]){if($online){return 'online'};return 'offline'}
  return 'unknown'
}

function Invoke-OneBotStatusProbe {
  param([Parameter(Mandatory)][object]$Slot,[Parameter(Mandatory)][int]$TimeoutSeconds)

  $token=[Environment]::GetEnvironmentVariable($Slot.TokenEnvironmentName,'Process')
  if([string]::IsNullOrWhiteSpace($token)){$token=[Environment]::GetEnvironmentVariable($Slot.TokenEnvironmentName,'User')}
  if([string]::IsNullOrWhiteSpace($token)){return 'unknown'}
  $socket=[Net.WebSockets.ClientWebSocket]::new()
  $cancel=[Threading.CancellationTokenSource]::new()
  try{
    $cancel.CancelAfter([TimeSpan]::FromSeconds($TimeoutSeconds))
    $socket.Options.SetRequestHeader('Authorization',"Bearer $token")
    $socket.ConnectAsync([Uri]("ws://{0}:{1}/"-f$Slot.Host,$Slot.Port),$cancel.Token).GetAwaiter().GetResult()
    $request=[Text.Encoding]::UTF8.GetBytes('{"action":"get_status","echo":"alife-startup-probe"}')
    $socket.SendAsync([ArraySegment[byte]]::new($request),[Net.WebSockets.WebSocketMessageType]::Text,$true,$cancel.Token).GetAwaiter().GetResult()
    $buffer=New-Object byte[]4096
    $received=$socket.ReceiveAsync([ArraySegment[byte]]::new($buffer),$cancel.Token).GetAwaiter().GetResult()
    return ConvertTo-OneBotStatus ([Text.Encoding]::UTF8.GetString($buffer,0,$received.Count))
  }catch{return 'unknown'}
  finally{$cancel.Dispose();$socket.Dispose()}
}

function Get-NapCatSlotStatus {
  param([Parameter(Mandatory)][object]$Slot,[Parameter(Mandatory)][int]$TimeoutSeconds,[Parameter(Mandatory)][string]$LoginMode,[Parameter(Mandatory)][bool]$QrRequested)

  $hostRunning=Test-NapCatHost -Slot $Slot
  $portReachable=Test-OneBotPort -HostName $Slot.Host -Port $Slot.Port -TimeoutSeconds $TimeoutSeconds
  $oneBotStatus=if($portReachable){Invoke-OneBotStatusProbe -Slot $Slot -TimeoutSeconds $TimeoutSeconds}else{'unknown'}
  [pscustomobject]@{
    port=$Slot.Port
    loginMode=$LoginMode
    qrRequested=$QrRequested
    hostRunning=$hostRunning
    portReachable=$portReachable
    oneBotStatus=$oneBotStatus
    ready=($hostRunning-and$portReachable-and$oneBotStatus-eq'online')
  }
}

if($MyInvocation.InvocationName-ne'.'){
  $slots=Get-NapCatDualAccountPlan -NapCatRoot $NapCatRoot
  if($AccountPort-ne0){
    $slots=@($slots|Where-Object Port -eq $AccountPort)
    if($slots.Count-ne1){throw 'Requested account port was not found.'}
  }
  if($LoginMode-eq'Qr' -and($AccountPort-eq0-or-not$Interactive)){throw 'QR login requires -AccountPort 3001 or 3002 and -Interactive.'}
  if($RestartLaunchers){
    if(-not$Start){throw '-RestartLaunchers requires -Start.'}
    foreach($slot in $slots){
      Get-CimInstance Win32_Process -Filter "Name='NapCatWinBootMain.exe'"|
        Where-Object{$_.ExecutablePath-eq$slot.LaunchPath}|
        ForEach-Object{Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue}
    }
  }
  if($Start){
    foreach($slot in $slots){
      $entry=if($LoginMode-eq'Quick'){$slot.QuickEntry}else{$slot.QrEntry}
      Start-Process -FilePath $entry -WorkingDirectory (Split-Path -Parent $entry) -WindowStyle $(if($Interactive){'Normal'}else{'Hidden'})
    }
  }
  $accounts=if($Start){@($slots|ForEach-Object{Get-NapCatSlotStatus -Slot $_ -TimeoutSeconds $ProbeTimeoutSeconds -LoginMode $LoginMode -QrRequested ($LoginMode-eq'Qr')})}else{@()}
  [pscustomobject]@{accountCount=$slots.Count;ports=@($slots.Port);started=[bool]$Start;loginMode=$LoginMode;accounts=$accounts}|ConvertTo-Json -Depth 4
}
