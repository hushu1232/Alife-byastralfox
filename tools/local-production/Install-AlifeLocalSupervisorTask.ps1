param([switch]$Install,[switch]$Remove,[string]$PlanPath)
$TaskName='Alife Local Dual Account Supervisor'
function New-AlifeLocalSupervisorTaskSpec {
  param([string]$PlanPath)
  $supervisor=Join-Path $PSScriptRoot 'Start-AlifeLocalSupervisor.ps1'
  [pscustomobject]@{TaskName=$TaskName;Execute=(Get-Command powershell.exe).Source;Arguments="-NoProfile -ExecutionPolicy Bypass -File `"$supervisor`"";RunLevel='Limited';PlanPath=$PlanPath}
}
function Install-AlifeLocalSupervisorTask {
  param([Parameter(Mandatory)][string]$PlanPath)
  [Environment]::SetEnvironmentVariable('ALIFE_LOCAL_PRODUCTION_PLAN',[IO.Path]::GetFullPath($PlanPath),'User')
  $spec=New-AlifeLocalSupervisorTaskSpec $PlanPath
  $action=New-ScheduledTaskAction -Execute $spec.Execute -Argument $spec.Arguments
  $triggers=@(New-ScheduledTaskTrigger -AtLogOn;New-ScheduledTaskTrigger -AtStartup)
  $principal=New-ScheduledTaskPrincipal -UserId ([Environment]::UserName) -LogonType Interactive -RunLevel Limited
  Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Principal $principal -Force | Out-Null
}
if($Remove){Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false}
elseif($Install){if([string]::IsNullOrWhiteSpace($PlanPath)){throw 'PlanPath is required.'};Install-AlifeLocalSupervisorTask $PlanPath}
