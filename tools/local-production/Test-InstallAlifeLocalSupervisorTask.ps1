$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Install-AlifeLocalSupervisorTask.ps1')
function Assert-True($Value){if(-not $Value){throw 'Expected true.'}}
function Assert-False($Value){if($Value){throw 'Expected false.'}}
function Assert-Equal($Actual,$Expected){if($Actual-ne$Expected){throw "Expected '$Expected', got '$Actual'."}}
$registered=New-AlifeLocalSupervisorTaskSpec -PlanPath 'D:\Alife\config\local-production\accounts.local.json'
Assert-Equal $registered.TaskName 'Alife Local Dual Account Supervisor'
Assert-True ($registered.Execute -match 'powershell.exe|pwsh.exe')
Assert-False ($registered.Arguments -match 'TOKEN|Bearer|accounts\.local\.json')
Assert-Equal $registered.RunLevel 'Limited'
