param([string]$NapCatRoot='D:\NapCat',[ValidateSet('User','Process')][string]$EnvironmentTarget='User',[switch]$Apply)
$ErrorActionPreference='Stop'

function New-LocalOneBotToken {
  $bytes=New-Object byte[] 32;$rng=[Security.Cryptography.RandomNumberGenerator]::Create();try{$rng.GetBytes($bytes)}finally{$rng.Dispose()}
  ([BitConverter]::ToString($bytes)).Replace('-','').ToLowerInvariant()
}
function Set-JsonTokenValue($Node,[string]$Token){
  if($null-eq$Node){return}
  if($Node-is [System.Collections.IEnumerable] -and $Node-isnot [string] -and $Node-isnot [pscustomobject]){foreach($item in $Node){Set-JsonTokenValue $item $Token};return}
  foreach($property in @($Node.PSObject.Properties)){
    if($property.Name-eq'token'){$property.Value=$Token;$script:tokenChanged=$true}
    elseif($property.Value-is [pscustomobject] -or ($property.Value-is [System.Collections.IEnumerable] -and $property.Value-isnot [string])){Set-JsonTokenValue $property.Value $Token}
  }
}
function Initialize-NapCatDualAccountTokens {
  param([Parameter(Mandatory)][string]$NapCatRoot,[ValidateSet('User','Process')][string]$EnvironmentTarget='User',[switch]$Apply)
  $found=@();foreach($file in Get-ChildItem -LiteralPath $NapCatRoot -Recurse -File -Filter 'onebot11_*.json'){
    $raw=Get-Content -LiteralPath $file.FullName -Raw;$match=[regex]::Match($raw,'(?i)"port"\s*:\s*(3001|3002)');if($match.Success){$found+=[pscustomobject]@{File=$file;Port=[int]$match.Groups[1].Value;Raw=$raw}}
  }
  $found=@($found|Sort-Object Port -Unique);if($found.Count-ne2){throw 'Exactly the 3001 and 3002 OneBot configurations are required.'}
  $result=@();foreach($entry in $found){
    $isSet=[regex]::IsMatch($entry.Raw,'(?i)"token"\s*:\s*"[^"\s]+"');$state=if($isSet){'set'}else{'empty'}
    if($Apply){
      $token=New-LocalOneBotToken;$json=$entry.Raw|ConvertFrom-Json;$script:tokenChanged=$false;Set-JsonTokenValue $json $token;if(-not $script:tokenChanged){throw 'OneBot token field was not found.'}
      $temporary="$($entry.File.FullName).tmp";$json|ConvertTo-Json -Depth 50|Set-Content -LiteralPath $temporary -Encoding UTF8;Move-Item -LiteralPath $temporary -Destination $entry.File.FullName -Force
      $name=if($entry.Port-eq3001){'ALIFE_ACCOUNT_A_ONEBOT_TOKEN'}else{'ALIFE_ACCOUNT_B_ONEBOT_TOKEN'};[Environment]::SetEnvironmentVariable($name,$token,$EnvironmentTarget);$state='set'
    }
    $result+=[pscustomobject]@{port=$entry.Port;tokenState=$state;applied=[bool]$Apply}
  }
  return $result
}

if($MyInvocation.InvocationName-ne'.'){Initialize-NapCatDualAccountTokens -NapCatRoot $NapCatRoot -EnvironmentTarget $EnvironmentTarget -Apply:$Apply|ConvertTo-Json -Depth 3}
